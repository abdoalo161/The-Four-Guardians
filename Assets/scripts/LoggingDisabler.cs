using UnityEngine;

public class LoggingDisabler : MonoBehaviour
{
#if !UNITY_EDITOR
    private void Awake()
    {
        // Disable all Debug.Log output in player builds
        Debug.unityLogger.logEnabled = false;
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.None);
    }
#endif
}
