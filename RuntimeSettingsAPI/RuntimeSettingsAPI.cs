using HMLLibrary;
using RaftModLoader;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RuntimeSettingsAPI
{
    /// <summary>
    /// Runtime Settings API - A standalone mod that extends Extra Settings API
    /// Allows mods to disable/enable settings at runtime (visible but non-interactive).
    /// For hiding/showing settings at runtime, use Extra Settings API's built-in
    /// CheckSettingVisibility and HandleSettingVisible instead.
    /// Uses reflection to access ExtraSettingsAPI types at runtime.
    /// </summary>
    public class RuntimeSettingsAPI : Mod
    {
        private static RuntimeSettingsAPI instance;
        public static JsonModInfo modInfo;

        // Runtime state storage for each setting
        private static Dictionary<string, Dictionary<string, SettingRuntimeState>> modSettingsRuntimeState;
        private static bool isInitialized = false;

        // Reflected types from ExtraSettingsAPI (loaded at runtime)
        private static Type extraSettingsAPIType;
        private static FieldInfo modSettingsField;
        private static bool reflectionInitialized = false;

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
            Log("Runtime Settings API unloaded");
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

                extraSettingsAPIType = extraSettingsAssembly.GetType("_ExtraSettingsAPI.ExtraSettingsAPI");

                if (extraSettingsAPIType == null)
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

        #region Public API - Enable/Disable Settings

        /// <summary>
        /// Disable a setting (visible but grayed out/non-interactive)
        /// </summary>
        public static bool DisableSetting(string modName, string settingName)
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
                            EnableSetting(modName, settingName);
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
