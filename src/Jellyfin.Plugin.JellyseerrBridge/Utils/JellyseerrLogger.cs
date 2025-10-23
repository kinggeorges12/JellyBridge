using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using System.Linq;

namespace Jellyfin.Plugin.JellyseerrBridge.Utils;

/// <summary>
/// Jellyseerr-specific logger wrapper that implements ILogger<T> and handles debug logging configuration.
/// </summary>
/// <typeparam name="T">The type for which the logger is created</typeparam>
public class JellyseerrLogger<T> : ILogger<T>
{
    private readonly ILogger<T> _innerLogger;

    public JellyseerrLogger(ILogger<T> innerLogger)
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
    /// Logs a trace message. If debug logging is enabled in config, logs as Information, otherwise logs as Trace.
    /// Limits output to 100 characters maximum.
    /// </summary>
    /// <param name="message">The message to log</param>
    /// <param name="args">Optional message arguments</param>
    public void LogTrace(string message, params object?[] args)
    {
        LogTraceInternal(null, message, args);
    }

    /// <summary>
    /// Logs a trace message with exception details.
    /// Limits output to 100 characters maximum.
    /// </summary>
    /// <param name="exception">The exception to log</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Optional message arguments</param>
    public void LogTrace(Exception exception, string message, params object?[] args)
    {
        LogTraceInternal(exception, message, args);
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
    /// <param name="exception">The exception to log (null if no exception)</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Optional message arguments</param>
    private void LogTraceInternal(Exception? exception, string message, object?[] args)
    {
        var config = Plugin.GetConfiguration();
        var enableDebugLogging = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableDebugLogging), config);
        
        if (enableDebugLogging)
        {
            try
            {
                // When debug logging is enabled, try to format first, then limit and prefix
                string formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
            
                var limitedMessage = LimitMessageToLinesAndCharacters(formattedMessage, 10, 100);
                var prefixedMessage = "[TRACE] " + limitedMessage;
                
                if (exception != null)
                    _innerLogger.LogInformation(exception, prefixedMessage);
                else
                    _innerLogger.LogInformation(prefixedMessage);
            }
            catch (FormatException)
            {
                // If formatting fails, let the logger handle it with the original message and args
                var prefixedMessage = "[TRACE] " + message;
                
                if (exception != null)
                    _innerLogger.LogInformation(exception, prefixedMessage, args);
                else
                    _innerLogger.LogInformation(prefixedMessage, args);
                return;
            }
        }
        else
        {
            // When debug logging is disabled, use full structured logging without limiting
            if (exception != null)
                _innerLogger.LogTrace(exception, message, args);
            else
                _innerLogger.LogTrace(message, args);
        }
    }

    /// <summary>
    /// Limits a message to the specified number of lines and characters per line by truncating if necessary.
    /// If maxLines or maxCharactersPerLine is 0, that limit is considered unlimited.
    /// </summary>
    /// <param name="message">The message to limit</param>
    /// <param name="maxLines">Maximum number of lines to keep (0 = unlimited)</param>
    /// <param name="maxCharactersPerLine">Maximum number of characters per line (0 = unlimited)</param>
    /// <returns>The limited message</returns>
    private static string LimitMessageToLinesAndCharacters(string message, int maxLines, int maxCharactersPerLine)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        var lines = message.Split('\n');
        
        // Apply line limit if maxLines > 0
        var linesToProcess = maxLines > 0 ? lines.Take(maxLines) : lines;
        
        // Apply character limit per line if maxCharactersPerLine > 0
        var limitedLines = linesToProcess.Select(line => 
        {
            if (maxCharactersPerLine > 0 && line.Length > maxCharactersPerLine)
                return line.Substring(0, maxCharactersPerLine) + "...";
            return line;
        }).ToArray();

        var result = string.Join("\n", limitedLines);
        
        // Add truncation notice if lines were removed
        if (maxLines > 0 && lines.Length > maxLines)
        {
            result += $"\n... [TRUNCATED] {lines.Length - maxLines} lines";
        }

        return result;
    }

}
