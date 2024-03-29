using System;
using System.Collections.Generic;
using System.IO;
using Nefta.Core.Data;
using UnityEngine;
using UnityEditor;

namespace Nefta.Core.Editor
{
    public class NeftaEditorWindow : EditorWindow
    {
        private const string SdkDevelopmentSymbol = "NEFTA_SDK_DBG";
        private const string SdkReleaseSymbol = "NEFTA_SDK_REL";

        private static NeftaConfiguration _configuration;
        private static NeftaEditorWindow _instance;
        private static List<INeftaEditorModule> _editorModules;
        
        private Dictionary<string, Action> _pages;
        private string _selectedPage;
        private Color _originalBackgroundColor;
        private Texture2D _logo;
        private bool _isLoggingEnabled;
        private bool _isEventRecordingEnabled;
        private GUIContent _androidAppIdLabel;
        private GUIContent _iOSAppIdLabel;

        private static NeftaEditorWindow Instance;

        [InitializeOnLoadMethod]
        private static void Init()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Init;
                return;
            }
            EditorApplication.delayCall -= Init;
            
            var defines = GetDefines();
            var isDevelopment = defines.Contains(SdkDevelopmentSymbol);
            var isRelease = defines.Contains(SdkReleaseSymbol);
            if (!isDevelopment && !isRelease)
            {
                SetSymbolEnabled(SdkDevelopmentSymbol, true);
            }
            GetConfiguration();
        }

        public static void RegisterModule(INeftaEditorModule module)
        {
            _editorModules ??= new List<INeftaEditorModule>();
            if (!_editorModules.Contains(module))
            {
                _editorModules.Add(module);
            }
        }
        
        public static NeftaConfiguration GetConfiguration()
        {
            if (_configuration != null)
            {
                return _configuration;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return null;
            } 
            
            var directory = "Assets/Resources";
            var assetPath = $"{directory}/{NeftaConfiguration.FileName}.asset";
            _configuration = AssetDatabase.LoadAssetAtPath<NeftaConfiguration>(assetPath);
            if (_configuration == null)
            {
                _configuration = CreateInstance<NeftaConfiguration>();
                _configuration._configurations = new List<NeftaModuleConfiguration>();

                _configuration._isEventRecordingEnabledOnStart = true;
                _configuration._isLoggingEnabled = true;
                
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                AssetDatabase.CreateAsset(_configuration, assetPath);
                AssetDatabase.SaveAssets();
            }

            return _configuration;
        }

        private static void OpenNeftaSDKWindow(string page)
        {
            _instance = (NeftaEditorWindow)GetWindow(typeof(NeftaEditorWindow), true, "Nefta SDK");
            _instance.minSize = new Vector2(580f, 280f);
            _instance._selectedPage = page ?? "Welcome";
            _instance.Show();

            GetConfiguration();
        }

        public static string GetDefines()
        {
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(GetNamedBuildTarget());
        }

        public static void SetSymbolEnabled(string symbol, bool enabled)
        {
            var defines = GetDefines();
            var symbolIndex = defines.IndexOf(symbol, StringComparison.InvariantCulture);
            if (enabled)
            {
                if (string.IsNullOrEmpty(defines))
                {
                    defines = symbol;
                }
                else if (symbolIndex < 0)
                {
                    defines += $";{symbol}";
                }
            }
            else
            {
                if (symbolIndex == 0)
                {
                    defines = defines.Replace(symbol, "");
                }
                else if (symbolIndex > 0)
                {
                    defines = defines.Replace($";{symbol}", "");
                }
            }

            var activeTarget = GetNamedBuildTarget();
            if (activeTarget != BuildTargetGroup.Android)
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, defines);
            }
            if (activeTarget != BuildTargetGroup.iOS)
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.iOS, defines);
            }
            PlayerSettings.SetScriptingDefineSymbolsForGroup(activeTarget, defines);
        }

        public static void EnableLogging(bool isEnabled)
        {
            if (Instance != null)
            {
                Instance._isLoggingEnabled = isEnabled;   
            }
            
            SetSymbolEnabled(SdkDevelopmentSymbol, isEnabled);
            SetSymbolEnabled(SdkReleaseSymbol, !isEnabled);
            
            var guid = AssetDatabase.FindAssets("NeftaPlugin-debug")[0];
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (PluginImporter) AssetImporter.GetAtPath(path);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, isEnabled);
            importer.SaveAndReimport();
            
            guid = AssetDatabase.FindAssets("NeftaPlugin-release")[0];
            path = AssetDatabase.GUIDToAssetPath(guid);
            importer = (PluginImporter) AssetImporter.GetAtPath(path);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, !isEnabled);
            importer.SaveAndReimport();

            var configuration = GetConfiguration();
            configuration._isLoggingEnabled = isEnabled;
            EditorUtility.SetDirty(_configuration);
            AssetDatabase.SaveAssetIfDirty(configuration);
        }

        [MenuItem("Window/Nefta SDK")]
        private static void OpenNeftaSDKWindow()
        {
            OpenNeftaSDKWindow(null);
        }

        private void Initialize()
        {
            Instance = this;
            
            var logoPath = AssetDatabase.GUIDToAssetPath("972cbec602f9a44089f7ec035d5564e4");
            _logo = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath);
            
            _androidAppIdLabel =  EditorGUIUtility.IconContent("BuildSettings.Android On");
            _androidAppIdLabel.text = " Android AppId";
            
            _iOSAppIdLabel =  EditorGUIUtility.IconContent("BuildSettings.iPhone On");
            _iOSAppIdLabel.text = " iOS AppId";

            Instance._isLoggingEnabled = GetDefines().Contains(SdkDevelopmentSymbol);

            _pages = new Dictionary<string, Action>
            {
                { "Welcome", OnWelcomePage },
                { "Core", OnCorePage },
                { "Utility", OnUtilityPage },
            };

            if (_editorModules != null)
            {
                foreach (var module in _editorModules)
                {
                    module.AddPages(_pages);
                }
            }
        }

        private void OnGUI()
        {
            if (_pages == null)
            {
                Initialize(); 
            }
            
            const float padding = 4;
            var windowRect = position;
            var menuRect = new Rect(padding, 110, 200, windowRect.height - padding);
            var pageRect = new Rect(padding + menuRect.width + padding, 110, windowRect.width - menuRect.width - padding * 3, windowRect.height - padding);

            _originalBackgroundColor = GUI.backgroundColor;
            
            GUI.DrawTexture(new Rect(windowRect.width * 0.5f - 128, 0, 256, 100), _logo);
            
            GUILayout.BeginArea(menuRect);
            foreach (var page in _pages)
            {
                OnMenuButton(page.Key);
            }
            GUILayout.EndArea();

            GUILayout.BeginArea(pageRect);
            _pages[_selectedPage]();
            GUILayout.EndArea();
        }

        private void OnWelcomePage()
        {
            GUILayout.Label("Welcome to Nefta SDK.");
            
            if (GUILayout.Button("Getting started"))
            {
                Application.OpenURL("https://docs-adnetwork.nefta.io/docs/nefta-unity-ad-sdk");    
            }
        }

        private void OnCorePage()
        {
            var appId = EditorGUILayout.TextField(_androidAppIdLabel, _configuration._androidAppId);
            if (appId != _configuration._androidAppId)
            {
                _configuration._androidAppId = appId;
                UpdateConfigurationOnDisk();
            }
            appId = EditorGUILayout.TextField(_iOSAppIdLabel, _configuration._iOSAppId);
            if (appId != _configuration._iOSAppId)
            {
                _configuration._iOSAppId = appId;
                UpdateConfigurationOnDisk();
            }
            
            GUILayout.Space(5);
            var isEventRecordingEnabled = GUILayout.Toggle(_configuration._isEventRecordingEnabledOnStart, "Enable game event recording");
            if (isEventRecordingEnabled != _configuration._isEventRecordingEnabledOnStart)
            {
                _configuration._isEventRecordingEnabledOnStart = isEventRecordingEnabled;
                UpdateConfigurationOnDisk();
            }
            
            GUILayout.Space(5);
            var isLoggingEnabled = GUILayout.Toggle(_isLoggingEnabled, "Enable logging");
            if (isLoggingEnabled != _isLoggingEnabled)
            {
                EnableLogging(isLoggingEnabled);
            }
        }

        private void OnUtilityPage()
        {
            if (GUILayout.Button("Clear cached player In PlayerPrefs"))
            {
                NeftaPlugin.ClearPrefs();
                
                Debug.Log("Cached player data cleared");
            }
        }

        private void OnMenuButton(string page)
        {
            if (_selectedPage == page)
            {
                GUI.backgroundColor = Color.green;
            }
            if (GUILayout.Button(page))
            {
                _selectedPage = page;
            }
            GUI.backgroundColor = _originalBackgroundColor;
        }
        
        public static void UpdateConfigurationOnDisk()
        {
            EditorUtility.SetDirty(_configuration);
            AssetDatabase.SaveAssetIfDirty(_configuration);
        }

        private static BuildTargetGroup GetNamedBuildTarget()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            return BuildPipeline.GetBuildTargetGroup(target);
        }
    }   
}
