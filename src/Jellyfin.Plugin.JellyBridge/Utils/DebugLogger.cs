using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using System.Linq;
using System.Diagnostics;
using System.Reflection;

namespace Jellyfin.Plugin.JellyBridge.Utils;

/// <summary>
/// JellyBridge-specific logger wrapper that implements ILogger<T> and handles debug logging configuration.
/// This class is a workaround for the complications of using the Jellyfin built-in logger.
/// </summary>
/// <typeparam name="T">The type for which the logger is created</typeparam>
public class DebugLogger<T> : ILogger<T>
{
    private readonly ILogger<T> _innerLogger;

    public DebugLogger(ILogger<T> innerLogger)
    {
        _innerLogger = innerLogger;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _innerLogger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _innerLogger.Log(logLevel, eventId, state, exception, formatter);
    }

    /// <summary>
    /// Logs a debug message. If debug logging is enabled in config, logs as Information, otherwise logs as Debug.
    /// </summary>
    /// <param name="message">The message to log</param>
    /// <param name="args">Optional message arguments</param>
    public void LogDebug(string message, params object?[] args)
    {
        LogDebugInternal(null, message, args);
    }

    /// <summary>
    /// Logs a debug message with exception details.
    /// </summary>
    /// <param name="exception">The exception to log</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Optional message arguments</param>
    public void LogDebug(Exception exception, string message, params object?[] args)
    {
        LogDebugInternal(exception, message, args);
    }

    /// <summary>
    /// Logs a trace message. If trace logging is enabled in config, logs as Information, otherwise logs as Trace.
    /// Automatically includes the calling method name using reflection.
    /// </summary>
    /// <param name="message">The message to log</param>
    /// <param name="args">Optional message arguments</param>
    public void LogTrace(string message, params object?[] args)
    {
        var methodName = GetCallingMethodName();
        LogTraceInternal(methodName, null, message, args);
    }

    /// <summary>
    /// Logs a trace message with exception details.
    /// Automatically includes the calling method name using reflection.
    /// </summary>
    /// <param name="exception">The exception to log</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Optional message arguments</param>
    public void LogTrace(Exception exception, string message, params object?[] args)
    {
        var methodName = GetCallingMethodName();
        LogTraceInternal(methodName, exception, message, args);
    }

    /// <summary>
    /// Gets the calling method name using reflection (StackFrame).
    /// </summary>
    private string GetCallingMethodName()
    {
        try
        {
            var trace = new StackTrace(skipFrames: 1, fNeedFileInfo: false);
            var projectNamespace = "Jellyfin.Plugin.JellyBridge";

            for (int i = 0; i < trace.FrameCount; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                var declaringType = method?.DeclaringType;
                if (method == null || declaringType == null)
                {
                    continue;
                }

                // Only consider frames from our project (fast path)
                var ns = declaringType.Namespace ?? string.Empty;
                if (!ns.StartsWith(projectNamespace, StringComparison.Ordinal))
                {
                    continue;
                }

                // Skip the logger itself
                if (declaringType == typeof(DebugLogger<T>))
                {
                    continue;
                }

                string typeName = declaringType.Name;
                string methodName = method.Name;

                // Normalize async MoveNext state-machine frames
                if (methodName == "MoveNext")
                {
                    var generated = typeName; // e.g., Class+<Method>d__12
                    var lt = generated.IndexOf('<');
                    var gt = generated.IndexOf('>');
                    if (lt >= 0 && gt > lt)
                    {
                        methodName = generated.Substring(lt + 1, gt - lt - 1);
                        if (declaringType.DeclaringType != null)
                        {
                            typeName = declaringType.DeclaringType.Name;
                        }
                    }
                }

                return $"{typeName}.{methodName}";
            }
        }
        catch { /* ignore and fall through */ }

        return string.Empty;
    }

    // LogInformation, LogWarning, and LogError just pass through to the inner logger
    public void LogInformation(string message, params object?[] args) => _innerLogger.LogInformation(message, args);
    public void LogInformation(Exception exception, string message, params object?[] args) => _innerLogger.LogInformation(exception, message, args);
    
    public void LogWarning(string message, params object?[] args) => _innerLogger.LogWarning(message, args);
    public void LogWarning(Exception exception, string message, params object?[] args) => _innerLogger.LogWarning(exception, message, args);
    
    public void LogError(string message, params object?[] args) => _innerLogger.LogError(message, args);
    public void LogError(Exception exception, string message, params object?[] args) => _innerLogger.LogError(exception, message, args);

    /// <summary>
    /// Internal method to handle debug logging with optional exception.
    /// </summary>
    /// <param name="exception">The exception to log (null if no exception)</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Optional message arguments</param>
    private void LogDebugInternal(Exception? exception, string message, object?[] args)
    {
        var config = Plugin.GetConfiguration();
        var enableDebugLogging = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableDebugLogging), config);
        
        if (enableDebugLogging)
        {
            // When debug logging is enabled, we need to add the prefix and log as info
            var prefixedMessage = "[DEBUG] " + message;
            
            if (exception != null)
                _innerLogger.LogInformation(exception, prefixedMessage, args);
            else
                _innerLogger.LogInformation(prefixedMessage, args);
        }
        else
        {
            if (exception != null)
                _innerLogger.LogDebug(exception, message, args);
            else
                _innerLogger.LogDebug(message, args);
        }
    }

    /// <summary>
    /// Internal method to handle trace logging with optional exception.
    /// </summary>
    /// <param name="memberName">The calling method name</param>
    /// <param name="exception">The exception to log (null if no exception)</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Optional message arguments</param>
    private void LogTraceInternal(string memberName, Exception? exception, string message, object?[] args)
    {
        var config = Plugin.GetConfiguration();
        var enableTraceLogging = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableTraceLogging), config);
        var enableDebugLogging = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableDebugLogging), config);
        
        // Trace logging requires debug logging to be enabled (enforced by UI, but check here too)
        if (enableTraceLogging && enableDebugLogging)
        {
            var formattedMethodPrefix = !string.IsNullOrEmpty(memberName) ? $"[{memberName}] " : "";
            
            // When trace logging is enabled, we need to add the prefix and log as info
            var prefixedMessage = "[TRACE] " + formattedMethodPrefix + message;
            
            if (exception != null)
                _innerLogger.LogInformation(exception, prefixedMessage, args);
            else
                _innerLogger.LogInformation(prefixedMessage, args);
        }
        else
        {
            if (exception != null)
                _innerLogger.LogTrace(exception, message, args);
            else
                _innerLogger.LogTrace(message, args);
        }
    }

}
