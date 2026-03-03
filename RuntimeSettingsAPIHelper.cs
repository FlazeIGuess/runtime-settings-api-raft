using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// RuntimeSettingsAPI Helper - Copy this file into your mod to use RuntimeSettingsAPI
///
/// This helper silently waits for RuntimeSettingsAPI to be ready in the background and
/// provides a simple interface for hiding/showing/enabling/disabling settings at runtime.
///
/// Usage:
///   1. Copy this file into your mod's source folder
///   2. Add "RuntimeSettingsAPI" as optional dependency in your modinfo.json
///   3. Use the helper methods from your Mod class
///
/// Example:
///   RuntimeSettingsAPIHelper.HideSection(this, "MyMod", "advancedSection");
/// </summary>
public static class RuntimeSettingsAPIHelper
{
    // The reflected RuntimeSettingsAPI type ("RuntimeSettingsAPI.RuntimeSettingsAPI")
    private static Type apiType;
    // True once we have successfully located the apiType in any loaded assembly
    private static bool available = false;

    // -------------------------------------------------------------------------
    // Locating the API — completely silent, retried each coroutine tick until found
    // -------------------------------------------------------------------------

    /// <summary>Returns true when the RuntimeSettingsAPI assembly is loaded and the type is found.</summary>
    public static bool IsAvailable()
    {
        if (!available)
            TryLocate();
        return available;
    }

    /// <summary>Returns true when RuntimeSettingsAPI has also found ExtraSettingsAPI and is fully ready.</summary>
    public static bool IsReady()
    {
        if (!IsAvailable())
            return false;

        try
        {
            var method = apiType.GetMethod("IsReady", BindingFlags.Public | BindingFlags.Static);
            return method != null && (bool)method.Invoke(null, null);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Scans all currently loaded assemblies for "RuntimeSettingsAPI.RuntimeSettingsAPI".
    /// Completely silent — writes nothing to the console.
    /// </summary>
    private static void TryLocate()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType("RuntimeSettingsAPI.RuntimeSettingsAPI");
                    if (t != null)
                    {
                        apiType  = t;
                        available = true;
                        return;
                    }
                }
                catch { /* skip assemblies that cannot be inspected */ }
            }
        }
        catch { }
    }

    // -------------------------------------------------------------------------
    // Public API — all operations wait silently in the background
    // -------------------------------------------------------------------------

    /// <param name="mono">MonoBehaviour to run the coroutine on (pass 'this' from your Mod)</param>
    /// <param name="modName">Exact mod name as declared in modinfo.json</param>
    /// <param name="settingName">Setting name key from modinfo.json</param>
    /// <param name="timeoutSeconds">How long to wait for the API before giving up (default 30 s)</param>
    public static void HideSetting(MonoBehaviour mono, string modName, string settingName, float timeoutSeconds = 30f)
        => mono.StartCoroutine(OperationCoroutine("HideSetting", modName, settingName, timeoutSeconds));

    public static void ShowSetting(MonoBehaviour mono, string modName, string settingName, float timeoutSeconds = 30f)
        => mono.StartCoroutine(OperationCoroutine("ShowSetting", modName, settingName, timeoutSeconds));

    public static void HideSection(MonoBehaviour mono, string modName, string sectionName, float timeoutSeconds = 30f)
        => mono.StartCoroutine(OperationCoroutine("HideSection", modName, sectionName, timeoutSeconds));

    public static void ShowSection(MonoBehaviour mono, string modName, string sectionName, float timeoutSeconds = 30f)
        => mono.StartCoroutine(OperationCoroutine("ShowSection", modName, sectionName, timeoutSeconds));

    public static void DisableSetting(MonoBehaviour mono, string modName, string settingName, float timeoutSeconds = 30f)
        => mono.StartCoroutine(OperationCoroutine("DisableSetting", modName, settingName, timeoutSeconds));

    public static void EnableSetting(MonoBehaviour mono, string modName, string settingName, float timeoutSeconds = 30f)
        => mono.StartCoroutine(OperationCoroutine("EnableSetting", modName, settingName, timeoutSeconds));

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generic coroutine: waits for the API to be ready, then invokes the named static method.
    /// Only writes to the console on success or on final timeout — never during the wait.
    /// </summary>
    private static IEnumerator OperationCoroutine(string methodName, string modName, string targetName, float timeoutSeconds)
    {
        yield return WaitForReady(timeoutSeconds);

        if (!IsReady())
        {
            // Only log if RuntimeSettingsAPI is genuinely not installed (available == false).
            // If it IS installed but not ready yet, something else went wrong — log that.
            if (!available)
                Debug.LogWarning($"[RuntimeSettingsAPIHelper] RuntimeSettingsAPI is not installed — {methodName}('{targetName}') skipped.");
            else
                Debug.LogWarning($"[RuntimeSettingsAPIHelper] RuntimeSettingsAPI did not become ready within {timeoutSeconds}s — {methodName}('{targetName}') skipped.");
            yield break;
        }

        try
        {
            var method = apiType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                Debug.LogError($"[RuntimeSettingsAPIHelper] Method '{methodName}' not found on RuntimeSettingsAPI.");
                yield break;
            }

            bool success = (bool)method.Invoke(null, new object[] { modName, targetName });
            if (success)
                RefreshUI();
            else
                Debug.LogWarning($"[RuntimeSettingsAPIHelper] {methodName}('{modName}', '{targetName}') returned false");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RuntimeSettingsAPIHelper] {methodName} error: {e.Message}");
        }
    }

    /// <summary>
    /// Silently polls until IsReady() or timeout. No console output during the wait.
    /// </summary>
    private static IEnumerator WaitForReady(float timeoutSeconds)
    {
        float elapsed = 0f;
        while (elapsed < timeoutSeconds)
        {
            // Retry the assembly scan every tick until we find it
            if (!available)
                TryLocate();

            if (IsReady())
                yield break;

            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        // Timeout reached — callers decide whether to log
    }

    private static void RefreshUI()
    {
        try
        {
            var method = apiType?.GetMethod("RefreshSettingsUI", BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, null);
        }
        catch { }
    }
}
