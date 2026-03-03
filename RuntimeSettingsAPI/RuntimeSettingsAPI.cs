using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RuntimeSettingsAPI
{
    /// <summary>
    /// Runtime Settings API - A standalone mod that extends Extra Settings API
    /// Allows mods to dynamically show/hide/enable/disable settings at runtime
    /// Uses reflection to access ExtraSettingsAPI types at runtime
    /// Works by directly manipulating GameObject visibility (SetActive)
    /// </summary>
    public class RuntimeSettingsAPI : Mod
    {
        private static RuntimeSettingsAPI instance;
        public static JsonModInfo modInfo;

        // Runtime state storage for each setting
        private static Dictionary<string, Dictionary<string, SettingRuntimeState>> modSettingsRuntimeState;
        private static bool isInitialized = false;
        
        // Pending operations to apply when settings are loaded
        private static Dictionary<string, List<PendingOperation>> pendingOperations;

        // Reflected types from ExtraSettingsAPI (loaded at runtime)
        private static Type modSettingType;
        private static Type extraSettingsAPIType;
        private static FieldInfo modSettingsField;
        private static bool reflectionInitialized = false;
        
        // Harmony patch to persist visibility state across ToggleSettings calls
        private static Harmony harmonyInstance;
        
        private enum OperationType
        {
            HideSetting,
            ShowSetting,
            HideSection,
            ShowSection,
            DisableSetting,
            EnableSetting
        }
        
        private class PendingOperation
        {
            public OperationType Type;
            public string TargetName;
        }

        public void Start()
        {
            instance = this;
            modInfo = modlistEntry.jsonmodinfo;
            
            EnsureInitialized();
            StartCoroutine(WaitForExtraSettingsAPI());
        }

        /// <summary>
        /// Wait for ExtraSettingsAPI to be loaded before initializing
        /// </summary>
        private System.Collections.IEnumerator WaitForExtraSettingsAPI()
        {
            int attempts = 0;
            const int maxAttempts = 300; // 30 seconds max

            while (attempts < maxAttempts)
            {
                if (InitializeReflection())
                    yield break;

                attempts++;
                yield return new WaitForSeconds(0.1f);
            }

            LogError("Failed to find ExtraSettingsAPI after 30 seconds. Make sure ExtraSettingsAPI is installed.");
        }

        public void OnDestroy()
        {
            harmonyInstance?.UnpatchAll(harmonyInstance.Id);
            harmonyInstance = null;
            Log("Runtime Settings API unloaded");
        }

        /// <summary>
        /// Apply a Harmony postfix to ModSettingContainer.ToggleSettings(bool on).
        /// ExtraSettingsAPI's ToggleSettings re-shows all settings via ShouldShow();
        /// our postfix immediately re-hides any setting that RuntimeSettingsAPI has marked hidden,
        /// so the hidden state persists across section collapses/expands.
        /// </summary>
        private static void ApplyHarmonyPatch()
        {
            try
            {
                var containerType = extraSettingsAPIType.Assembly.GetType("_ExtraSettingsAPI.ModSettingContainer");
                if (containerType == null)
                {
                    LogError("Could not find ModSettingContainer type for Harmony patch");
                    return;
                }

                var toggleMethod = containerType.GetMethod("ToggleSettings", new Type[] { typeof(bool) });
                if (toggleMethod == null)
                {
                    LogError("Could not find ToggleSettings(bool) method for Harmony patch");
                    return;
                }

                harmonyInstance = new Harmony("com.RuntimeSettingsAPI.patch");
                var postfix = typeof(RuntimeSettingsAPI).GetMethod(
                    nameof(ToggleSettings_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                harmonyInstance.Patch(toggleMethod, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception e)
            {
                LogError($"Failed to apply Harmony patch to ToggleSettings: {e}");
            }
        }

        /// <summary>
        /// Harmony postfix for ModSettingContainer.ToggleSettings(bool on).
        /// Runs AFTER ExtraSettingsAPI has re-shown settings via ShouldShow().
        /// Re-hides any setting (or setting in a hidden section) that RuntimeSettingsAPI has marked hidden.
        /// </summary>
        private static void ToggleSettings_Postfix(object __instance, bool on)
        {
            // Only need to re-hide when the section is being shown (on = true)
            if (!on || !isInitialized) return;

            try
            {
                var modNameField = __instance.GetType().GetField("ModName");
                var modName = modNameField?.GetValue(__instance) as string;
                if (string.IsNullOrEmpty(modName)) return;

                if (!modSettingsRuntimeState.TryGetValue(modName, out var settingStates)) return;

                var allSettingsField = __instance.GetType().GetField("allSettings");
                var allSettings = allSettingsField?.GetValue(__instance) as System.Collections.IList;
                if (allSettings == null) return;

                foreach (var setting in allSettings)
                {
                    var nameField = setting.GetType().GetField("name");
                    var name = nameField?.GetValue(setting) as string ?? "";

                    var sectionField = setting.GetType().GetField("section");
                    var section = sectionField?.GetValue(setting) as string;

                    var controlProp = setting.GetType().GetProperty("control");
                    var control = controlProp?.GetValue(setting) as GameObject;
                    if (control == null) continue;

                    // Hide if this setting itself is marked hidden
                    if (!string.IsNullOrEmpty(name)
                        && settingStates.TryGetValue(name, out var nameState)
                        && !nameState.IsVisible)
                    {
                        control.SetActive(false);
                        continue;
                    }

                    // Hide if this setting belongs to a hidden section
                    // (also covers unnamed text/separator items that cannot be tracked by name)
                    if (!string.IsNullOrEmpty(section)
                        && settingStates.TryGetValue(section, out var sectionState)
                        && !sectionState.IsVisible)
                    {
                        control.SetActive(false);
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"Error in ToggleSettings postfix: {e.Message}");
            }
        }

        /// <summary>
        /// Initialize reflection to access ExtraSettingsAPI types. Silent until success or hard failure.
        /// </summary>
        private static bool InitializeReflection()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Assembly extraSettingsAssembly = null;

                foreach (var asm in assemblies)
                {
                    try
                    {
                        var types = asm.GetTypes();
                        foreach (var type in types)
                        {
                            if (type.Namespace == "_ExtraSettingsAPI" && type.Name == "ExtraSettingsAPI")
                            {
                                extraSettingsAssembly = asm;
                                break;
                            }
                        }
                        if (extraSettingsAssembly != null)
                            break;
                    }
                    catch { continue; }
                }

                if (extraSettingsAssembly == null)
                    return false; // still waiting — silent

                modSettingType = extraSettingsAssembly.GetType("_ExtraSettingsAPI.ModSetting");
                extraSettingsAPIType = extraSettingsAssembly.GetType("_ExtraSettingsAPI.ExtraSettingsAPI");

                if (modSettingType == null || extraSettingsAPIType == null)
                {
                    LogError("ExtraSettingsAPI found but required types are missing — cannot initialize.");
                    return false;
                }

                modSettingsField = extraSettingsAPIType.GetField("modSettings", BindingFlags.Public | BindingFlags.Static);
                if (modSettingsField == null)
                {
                    LogError("ExtraSettingsAPI found but 'modSettings' field is missing — cannot initialize.");
                    return false;
                }

                reflectionInitialized = true;
                Log("Runtime Settings API ready.");
                ApplyHarmonyPatch();
                return true;
            }
            catch (Exception e)
            {
                LogError($"Error initializing reflection: {e}");
                return false;
            }
        }

        private static void EnsureInitialized()
        {
            if (!isInitialized)
            {
                modSettingsRuntimeState = new Dictionary<string, Dictionary<string, SettingRuntimeState>>();
                pendingOperations = new Dictionary<string, List<PendingOperation>>();
                isInitialized = true;
            }
        }

        #region Logging

        public static void Log(object message)
        {
            Debug.Log($"[{modInfo?.name ?? "RuntimeSettingsAPI"}]: {message}");
        }

        public static void LogWarning(object message)
        {
            Debug.LogWarning($"[{modInfo?.name ?? "RuntimeSettingsAPI"}]: {message}");
        }

        public static void LogError(object message)
        {
            Debug.LogError($"[{modInfo?.name ?? "RuntimeSettingsAPI"}]: {message}");
        }

        #endregion

        #region Runtime State Management

        private class SettingRuntimeState
        {
            public bool IsVisible { get; set; } = true;
            public bool IsEnabled { get; set; } = true;
            public bool WasModified { get; set; } = false;
        }

        private static SettingRuntimeState GetOrCreateState(string modName, string settingName)
        {
            EnsureInitialized();

            if (!modSettingsRuntimeState.ContainsKey(modName))
                modSettingsRuntimeState[modName] = new Dictionary<string, SettingRuntimeState>();

            if (!modSettingsRuntimeState[modName].ContainsKey(settingName))
                modSettingsRuntimeState[modName][settingName] = new SettingRuntimeState();

            return modSettingsRuntimeState[modName][settingName];
        }

        private static SettingRuntimeState TryGetState(string modName, string settingName)
        {
            EnsureInitialized();

            if (modSettingsRuntimeState.TryGetValue(modName, out var modStates))
                if (modStates.TryGetValue(settingName, out var state))
                    return state;

            return null;
        }

        #endregion

        #region Helper Methods

        private static void AddPendingOperation(string modName, OperationType type, string targetName)
        {
            EnsureInitialized();
            
            if (!pendingOperations.ContainsKey(modName))
                pendingOperations[modName] = new List<PendingOperation>();
            
            pendingOperations[modName].Add(new PendingOperation
            {
                Type = type,
                TargetName = targetName
            });
        }
        
        private static void ApplyPendingOperations(string modName)
        {
            if (!pendingOperations.ContainsKey(modName))
                return;
            
            var operations = pendingOperations[modName];
            
            foreach (var op in operations)
            {
                switch (op.Type)
                {
                    case OperationType.HideSetting:
                        HideSettingInternal(modName, op.TargetName);
                        break;
                    case OperationType.ShowSetting:
                        ShowSetting(modName, op.TargetName);
                        break;
                    case OperationType.HideSection:
                        HideSectionInternal(modName, op.TargetName);
                        break;
                    case OperationType.ShowSection:
                        ShowSection(modName, op.TargetName);
                        break;
                    case OperationType.DisableSetting:
                        DisableSetting(modName, op.TargetName);
                        break;
                    case OperationType.EnableSetting:
                        EnableSetting(modName, op.TargetName);
                        break;
                }
            }
            
            pendingOperations.Remove(modName);
            RefreshSettingsUI();
        }
        
        /// <summary>
        /// Call this method to manually trigger pending operations for a mod
        /// Useful if you know your settings have been loaded
        /// </summary>
        public static void ApplyPendingOperationsForMod(string modName)
        {
            ApplyPendingOperations(modName);
        }

        private static Mod FindMod(string modName)
        {
            try
            {
                var modSettings = modSettingsField.GetValue(null);
                if (modSettings == null) return null;

                // modSettings is Dictionary<Mod, ModSettingContainer>
                var dict = modSettings as System.Collections.IDictionary;
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    var mod = entry.Key as Mod;
                    var container = entry.Value;
                    
                    // Get ModName field (readonly fields, not properties)
                    var modNameField = container.GetType().GetField("ModName");
                    var idNameField = container.GetType().GetField("IDName");
                    
                    string containerModName = modNameField?.GetValue(container) as string;
                    string containerIDName = idNameField?.GetValue(container) as string;
                    
                    if (containerModName == modName || containerIDName == modName)
                        return mod;
                }
            }
            catch (Exception e)
            {
                LogError($"Error finding mod: {e}");
            }

            return null;
        }

        private static object FindSetting(Mod mod, string settingName)
        {
            if (mod == null) return null;

            try
            {
                var modSettings = modSettingsField.GetValue(null) as System.Collections.IDictionary;
                if (modSettings == null || !modSettings.Contains(mod))
                    return null;

                var container = modSettings[mod];
                
                // Get settings dictionary
                var settingsField = container.GetType().GetField("settings");
                var allSettingsField = container.GetType().GetField("allSettings");
                
                if (settingsField != null)
                {
                    var settings = settingsField.GetValue(container) as System.Collections.IDictionary;
                    if (settings != null && settings.Contains(settingName))
                        return settings[settingName];
                }

                // Fallback: search allSettings list
                if (allSettingsField != null)
                {
                    var allSettings = allSettingsField.GetValue(container) as System.Collections.IList;
                    if (allSettings != null)
                    {
                        foreach (var setting in allSettings)
                        {
                            var nameProp = setting.GetType().GetField("name");
                            if (nameProp != null)
                            {
                                var name = nameProp.GetValue(setting) as string;
                                if (name == settingName)
                                    return setting;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"Error finding setting: {e}");
            }

            return null;
        }

        private static List<object> GetSettingsInSection(Mod mod, string sectionName)
        {
            var result = new List<object>();
            if (mod == null) return result;

            try
            {
                var modSettings = modSettingsField.GetValue(null) as System.Collections.IDictionary;
                if (modSettings == null || !modSettings.Contains(mod))
                    return result;

                var container = modSettings[mod];
                var allSettingsField = container.GetType().GetField("allSettings");
                
                if (allSettingsField != null)
                {
                    var allSettings = allSettingsField.GetValue(container) as System.Collections.IList;
                    if (allSettings != null)
                    {
                        foreach (var setting in allSettings)
                        {
                            var sectionField = setting.GetType().GetField("section");
                            if (sectionField != null)
                            {
                                var section = sectionField.GetValue(setting) as string;
                                if (section == sectionName)
                                    result.Add(setting);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"Error getting settings in section: {e}");
            }

            return result;
        }

        #endregion

        #region Public API - Status Check

        /// <summary>
        /// Check if RuntimeSettingsAPI is ready to use
        /// </summary>
        public static bool IsReady()
        {
            return reflectionInitialized;
        }

        /// <summary>
        /// Wait for RuntimeSettingsAPI to be ready (use in coroutine)
        /// </summary>
        public static System.Collections.IEnumerator WaitUntilReady(float timeoutSeconds = 10f)
        {
            float elapsed = 0f;
            
            while (!reflectionInitialized && elapsed < timeoutSeconds)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }
        }

        #endregion

        #region Public API - Hide/Show Settings

        /// <summary>
        /// Hide a specific setting by name
        /// If the mod's settings are not loaded yet, the operation will be queued
        /// </summary>
        public static bool HideSetting(string modName, string settingName)
        {
            if (!reflectionInitialized)
            {
                LogWarning("Runtime Settings API not initialized yet - ExtraSettingsAPI may not be loaded");
                return false;
            }

            var mod = FindMod(modName);
            if (mod == null)
            {
                AddPendingOperation(modName, OperationType.HideSetting, settingName);
                return true;
            }

            return HideSettingInternal(modName, settingName);
        }
        
        private static bool HideSettingInternal(string modName, string settingName)
        {
            var mod = FindMod(modName);
            if (mod == null)
            {
                LogWarning($"Mod '{modName}' not found");
                return false;
            }

            var setting = FindSetting(mod, settingName);
            if (setting == null)
            {
                LogWarning($"Setting '{settingName}' not found in mod '{modName}'");
                return false;
            }

            var state = GetOrCreateState(modName, settingName);
            state.IsVisible = false;
            state.WasModified = true;

            // Update UI if control exists
            var controlProp = setting.GetType().GetProperty("control");
            if (controlProp != null)
            {
                var control = controlProp.GetValue(setting) as GameObject;
                if (control != null)
                    control.SetActive(false);
            }

            return true;
        }

        /// <summary>
        /// Show a previously hidden setting
        /// </summary>
        public static bool ShowSetting(string modName, string settingName)
        {
            if (!reflectionInitialized)
            {
                LogWarning("Runtime Settings API not initialized yet - ExtraSettingsAPI may not be loaded");
                return false;
            }

            var mod = FindMod(modName);
            if (mod == null)
            {
                LogWarning($"Mod '{modName}' not found");
                return false;
            }

            var setting = FindSetting(mod, settingName);
            if (setting == null)
            {
                LogWarning($"Setting '{settingName}' not found in mod '{modName}'");
                return false;
            }

            var state = GetOrCreateState(modName, settingName);
            state.IsVisible = true;
            state.WasModified = true;

            // Update UI if control exists
            var controlProp = setting.GetType().GetProperty("control");
            if (controlProp != null)
            {
                var control = controlProp.GetValue(setting) as GameObject;
                if (control != null)
                {
                    // Check if should be shown using ShouldShow method
                    var shouldShowMethod = setting.GetType().GetMethod("ShouldShow");
                    if (shouldShowMethod != null)
                    {
                        // Get IsInWorld
                        var isInWorldField = extraSettingsAPIType.GetField("IsInWorld", BindingFlags.Public | BindingFlags.Static);
                        bool isInWorld = isInWorldField != null && (bool)isInWorldField.GetValue(null);
                        
                        bool shouldShow = (bool)shouldShowMethod.Invoke(setting, new object[] { !isInWorld });
                        if (shouldShow)
                            control.SetActive(true);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Hide an entire section and all its child settings
        /// If the mod's settings are not loaded yet, the operation will be queued
        /// </summary>
        public static bool HideSection(string modName, string sectionName)
        {
            if (!reflectionInitialized)
            {
                LogWarning("Runtime Settings API not initialized yet - ExtraSettingsAPI may not be loaded");
                return false;
            }

            var mod = FindMod(modName);
            if (mod == null)
            {
                AddPendingOperation(modName, OperationType.HideSection, sectionName);
                return true;
            }

            return HideSectionInternal(modName, sectionName);
        }
        
        private static bool HideSectionInternal(string modName, string sectionName)
        {
            var mod = FindMod(modName);
            if (mod == null)
            {
                LogWarning($"Mod '{modName}' not found");
                return false;
            }

            var section = FindSetting(mod, sectionName);
            if (section != null)
                HideSettingInternal(modName, sectionName);

            var settingsInSection = GetSettingsInSection(mod, sectionName);
            foreach (var setting in settingsInSection)
            {
                var nameField = setting.GetType().GetField("name");
                if (nameField != null)
                {
                    var name = nameField.GetValue(setting) as string;
                    if (!string.IsNullOrEmpty(name))
                        HideSettingInternal(modName, name);
                }
            }

            return true;
        }

        /// <summary>
        /// Show an entire section and all its child settings
        /// </summary>
        public static bool ShowSection(string modName, string sectionName)
        {
            if (!reflectionInitialized)
            {
                LogWarning("Runtime Settings API not initialized yet - ExtraSettingsAPI may not be loaded");
                return false;
            }

            var mod = FindMod(modName);
            if (mod == null)
            {
                LogWarning($"Mod '{modName}' not found");
                return false;
            }

            var section = FindSetting(mod, sectionName);
            if (section != null)
                ShowSetting(modName, sectionName);

            var settingsInSection = GetSettingsInSection(mod, sectionName);
            foreach (var setting in settingsInSection)
            {
                var nameField = setting.GetType().GetField("name");
                if (nameField != null)
                {
                    var name = nameField.GetValue(setting) as string;
                    if (!string.IsNullOrEmpty(name))
                        ShowSetting(modName, name);
                }
            }

            return true;
        }

        #endregion

        #region Public API - Enable/Disable Settings

        /// <summary>
        /// Disable a setting (visible but grayed out/non-interactive)
        /// </summary>
        public static bool DisableSetting(string modName, string settingName)
        {
            var mod = FindMod(modName);
            if (mod == null)
            {
                LogWarning($"Mod '{modName}' not found");
                return false;
            }

            var setting = FindSetting(mod, settingName);
            if (setting == null)
            {
                LogWarning($"Setting '{settingName}' not found in mod '{modName}'");
                return false;
            }

            var state = GetOrCreateState(modName, settingName);
            state.IsEnabled = false;
            state.WasModified = true;

            ApplyDisabledState(setting, false);
            return true;
        }

        /// <summary>
        /// Enable a previously disabled setting
        /// </summary>
        public static bool EnableSetting(string modName, string settingName)
        {
            var mod = FindMod(modName);
            if (mod == null)
            {
                LogWarning($"Mod '{modName}' not found");
                return false;
            }

            var setting = FindSetting(mod, settingName);
            if (setting == null)
            {
                LogWarning($"Setting '{settingName}' not found in mod '{modName}'");
                return false;
            }

            var state = GetOrCreateState(modName, settingName);
            state.IsEnabled = true;
            state.WasModified = true;

            ApplyDisabledState(setting, true);
            return true;
        }

        private static void ApplyDisabledState(object setting, bool enabled)
        {
            try
            {
                var controlProp = setting.GetType().GetProperty("control");
                if (controlProp == null) return;

                var control = controlProp.GetValue(setting) as GameObject;
                if (control == null) return;

                var alpha = enabled ? 1f : 0.5f;
                var interactable = enabled;

                // Try to find and disable interactive components
                var toggle = control.GetComponentInChildren<UnityEngine.UI.Toggle>();
                if (toggle != null)
                {
                    toggle.interactable = interactable;
                    var canvasGroup = toggle.GetComponent<CanvasGroup>() ?? toggle.gameObject.AddComponent<CanvasGroup>();
                    canvasGroup.alpha = alpha;
                    return;
                }

                var slider = control.GetComponentInChildren<UnityEngine.UI.Slider>();
                if (slider != null)
                {
                    slider.interactable = interactable;
                    var canvasGroup = slider.GetComponent<CanvasGroup>() ?? slider.gameObject.AddComponent<CanvasGroup>();
                    canvasGroup.alpha = alpha;
                    return;
                }

                var dropdown = control.GetComponentInChildren<UnityEngine.UI.Dropdown>();
                if (dropdown != null)
                {
                    dropdown.interactable = interactable;
                    var canvasGroup = dropdown.GetComponent<CanvasGroup>() ?? dropdown.gameObject.AddComponent<CanvasGroup>();
                    canvasGroup.alpha = alpha;
                    return;
                }

                var button = control.GetComponentInChildren<UnityEngine.UI.Button>();
                if (button != null)
                {
                    button.interactable = interactable;
                    var canvasGroup = button.GetComponent<CanvasGroup>() ?? button.gameObject.AddComponent<CanvasGroup>();
                    canvasGroup.alpha = alpha;
                    return;
                }

                var inputField = control.GetComponentInChildren<UnityEngine.UI.InputField>();
                if (inputField != null)
                {
                    inputField.interactable = interactable;
                    var canvasGroup = inputField.GetComponent<CanvasGroup>() ?? inputField.gameObject.AddComponent<CanvasGroup>();
                    canvasGroup.alpha = alpha;
                }
            }
            catch (Exception e)
            {
                LogError($"Error applying disabled state: {e}");
            }
        }

        #endregion

        #region Public API - Query State

        /// <summary>
        /// Check if a setting is currently visible
        /// </summary>
        public static bool IsSettingVisible(string modName, string settingName)
        {
            var state = TryGetState(modName, settingName);
            return state?.IsVisible ?? true;
        }

        /// <summary>
        /// Check if a setting is currently enabled
        /// </summary>
        public static bool IsSettingEnabled(string modName, string settingName)
        {
            var state = TryGetState(modName, settingName);
            return state?.IsEnabled ?? true;
        }

        #endregion

        #region Public API - UI Refresh

        /// <summary>
        /// Force refresh the settings UI to reflect runtime changes
        /// </summary>
        public static void RefreshSettingsUI()
        {
            try
            {
                // settingsExist is a computed property (=> newTabBody), not a field
                var settingsExistProp = extraSettingsAPIType.GetProperty("settingsExist", BindingFlags.Public | BindingFlags.Static);
                if (settingsExistProp == null || !(bool)settingsExistProp.GetValue(null))
                {
                    LogWarning("Settings UI does not exist yet");
                    return;
                }

                var modSettings = modSettingsField.GetValue(null) as System.Collections.IDictionary;
                if (modSettings != null)
                {
                    foreach (System.Collections.DictionaryEntry entry in modSettings)
                    {
                        var container = entry.Value;
                        var toggleMethod = container.GetType().GetMethod("ToggleSettings", new Type[0]);
                        if (toggleMethod != null)
                            toggleMethod.Invoke(container, null);
                    }
                }

                var updateMethod = extraSettingsAPIType.GetMethod("UpdateAllSettingBacks", BindingFlags.Public | BindingFlags.Static);
                if (updateMethod != null)
                    updateMethod.Invoke(null, null);
            }
            catch (Exception e)
            {
                LogError($"Error refreshing UI: {e}");
            }
        }

        #endregion

        #region Public API - Reset State

        /// <summary>
        /// Reset runtime state for a specific setting
        /// </summary>
        public static bool ResetSettingState(string modName, string settingName)
        {
            EnsureInitialized();

            if (modSettingsRuntimeState.TryGetValue(modName, out var modStates))
            {
                if (modStates.Remove(settingName))
                {
                    var mod = FindMod(modName);
                    if (mod != null)
                    {
                        var setting = FindSetting(mod, settingName);
                        if (setting != null)
                        {
                            ShowSetting(modName, settingName);
                            EnableSetting(modName, settingName);
                        }
                    }
                    
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reset all runtime state for a mod
        /// </summary>
        public static bool ResetModState(string modName)
        {
            EnsureInitialized();

            if (modSettingsRuntimeState.Remove(modName))
            {
                RefreshSettingsUI();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clear all runtime state for all mods
        /// </summary>
        public static void ClearAllState()
        {
            EnsureInitialized();
            modSettingsRuntimeState.Clear();
            RefreshSettingsUI();
        }

        #endregion
    }
}
