using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;

namespace AllErrorsWindow
{
    public class AllProjectErrorsWindow : EditorWindow
    {
        private string[] allScriptFiles;
        private Dictionary<string, List<ErrorInfo>> fileErrors = new Dictionary<string, List<ErrorInfo>>();
        private bool showAllErrors = true;
        private int currentFilterIndex = 0;
        
        [MenuItem("Window/All Project Errors")]
        public static void ShowWindow()
        {
            var window = GetWindow<AllProjectErrorsWindow>("All Project Errors");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private class ErrorInfo
        {
            public string fileName;
            public int line;
            public string message;
            public string category; // "Error", "Warning"
            
            public override string ToString()
            {
                return $"[{category}] {fileName}({line}): {message}";
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            showAllErrors = EditorGUILayout.Toggle("Show All Errors", showAllErrors);
            currentFilterIndex = EditorGUILayout.Popup(currentFilterIndex, new[] { "All Files", "Scripts Only", "Open Files" });
            GUILayout.EndHorizontal();

            if (showAllErrors)
            {
                RefreshErrorData();
                DisplayAllErrors();
            }
            else
            {
                EditorGUILayout.HelpBox("Enable 'Show All Errors' to see errors from all files in the project.", MessageType.Info);
            }
        }

        private void RefreshErrorData()
        {
            fileErrors.Clear();
            
            // Get all C# script files
            var scripts = AssetDatabase.FindAssets("t:Script", null)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetExtension(p).ToLower() == ".cs")
                .ToList();
            
            // Get all open file GUIDs
            var openFiles = EditorWindow.focusedWindow != null ? 
                EditorWindow.focusedWindow.GetType().GetField("m_UnityEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(EditorWindow.focusedWindow) as object : null;
            
            foreach (var scriptPath in scripts)
            {
                var errors = GetErrorsForFile(scriptPath);
                if (errors.Any())
                {
                    fileErrors[scriptPath] = errors;
                }
            }
        }

        private List<ErrorInfo> GetErrorsForFile(string scriptPath)
        {
            var errors = new List<ErrorInfo>();
            
            // Check if file is open in Unity Editor
            bool isOpenInUnity = IsFileOpenInUnity(scriptPath);
            
            try
            {
                var compilationErrors = ScriptCompilation.GetCompilationErrors(scriptPath);
                
                foreach (var error in compilationErrors)
                {
                    errors.Add(new ErrorInfo
                    {
                        fileName = scriptPath,
                        line = error.line,
                        message = error.message,
                        category = error.type == ScriptCompilation.CompilationErrorType.Error ? "ERROR" : "WARNING"
                    });
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not read errors for {scriptPath}: {e.Message}");
            }
            
            return errors;
        }

        private bool IsFileOpenInUnity(string scriptPath)
        {
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(scriptPath);
                if (guid == "") return false;
                
                // Check Editor's open file list
                var editorWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.EditorWindow");
                var field = editorWindowType.GetField("m_UnityEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null) return false;
                
                var focusedWindow = EditorWindow.focusedWindow;
                if (focusedWindow == null) return false;
                
                var editorField = focusedWindow.GetType().GetField("m_UnityEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (editorField == null) return false;
                
                var editorValue = editorField.GetValue(focusedWindow);
                var openFilesField = editorValue.GetType().GetField("m_openFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (openFilesField == null) return false;
                
                var openFiles = openFilesField.GetValue(editorValue) as System.Collections.Generic.List<object>;
                if (openFiles == null) return false;
                
                foreach (var file in openFiles)
                {
                    if (file is string strFile)
                    {
                        if (string.Equals(strFile, scriptPath, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }
            
            return false;
        }

        private void DisplayAllErrors()
        {
            var sortedFiles = fileErrors.Keys.OrderBy(k => k).ToArray();
            
            if (sortedFiles.Length == 0)
            {
                EditorGUILayout.HelpBox("No errors found in the project.", MessageType.Info);
                return;
            }

            // Filter files based on selection
            var filteredFiles = sortedFiles.Where(f => currentFilterIndex == 2 ? IsFileOpenInUnity(f) : true).ToArray();
            
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Total Errors: {fileErrors.Values.Sum(e => e.Count)}", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            for (int i = 0; i < filteredFiles.Length; i++)
            {
                var filePath = filteredFiles[i];
                var errors = fileErrors[filePath];
                
                if (errors.Count == 0) continue;

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(Path.GetFileName(filePath), EditorStyles.boldLabel);
                
                foreach (var error in errors)
                {
                    var color = error.category == "ERROR" ? Color.red : Color.yellow;
                    EditorGUILayout.LabelField($"  {color}{error.line}: {error.message}", new GUIStyle(EditorStyles.label) { richText = true });
                }
                
                EditorGUILayout.EndVertical();
            }
        }
    }

    // Helper class to get compilation errors from a script file
    public static class ScriptCompilation
    {
        private const string CompilationErrorsPath = "C:\\Program Files\\Unity\\Hub\\Editor\\6000.4.1f1\\Editor\\Data\\Logs\\ScriptCompilation.log";
        
        public struct CompilationError
        {
            public int line;
            public string message;
            public CompilationErrorType type;
        }

        public enum CompilationErrorType { Error, Warning }

        public static List<CompilationError> GetCompilationErrors(string scriptPath)
        {
            var errors = new List<CompilationError>();
            
            try
            {
                // Read the compilation log file
                if (System.IO.File.Exists(CompilationErrorsPath))
                {
                    var lines = System.IO.File.ReadAllLines(CompilationErrorsPath);
                    foreach (var line in lines)
                    {
                        // Parse error lines: [ERROR] <script_path>:<line> <message>
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"\[ERROR\]\s+([^:]+):([0-9]+)\s+(.*)");
                        if (match.Success)
                        {
                            errors.Add(new CompilationError
                            {
                                line = int.Parse(match.Groups[2].Value),
                                message = match.Groups[3].Value,
                                type = CompilationErrorType.Error
                            });
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not read compilation log: {e.Message}");
            }
            
            return errors;
        }
    }
}