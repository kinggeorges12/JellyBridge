using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using System.Reflection;
using System.Diagnostics;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Generic configuration-aware wrapper that automatically handles configuration locking for any service.
/// This provides centralized configuration blocking without creating individual wrapper classes.
/// </summary>
/// <typeparam name="T">The service type to wrap</typeparam>
public class ConfigurationAwareService<T> : DispatchProxy where T : class
{
    private T? _target;
    private ILogger<ConfigurationAwareService<T>>? _logger;

    /// <summary>
    /// Creates a configuration-aware wrapper for the specified service.
    /// </summary>
    public static T Create(T target, ILogger<ConfigurationAwareService<T>> logger)
    {
        var proxy = Create<T, ConfigurationAwareService<T>>() as ConfigurationAwareService<T>;
        proxy!._target = target;
        proxy!._logger = logger;
        return (T)(object)proxy!;
    }

    /// <summary>
    /// Intercepts method calls and adds configuration locking.
    /// </summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null || _target == null || _logger == null)
        {
            return targetMethod?.Invoke(_target, args);
        }

        // Wait for operation lock if any operation is running
        if (Plugin.IsOperationRunning)
        {
            _logger.LogInformation("Another operation is running, waiting for lock before executing {ServiceType}.{MethodName}", 
                typeof(T).Name, targetMethod.Name);
            
            // Handle async methods
            if (targetMethod.ReturnType.IsAssignableTo(typeof(Task)))
            {
                return HandleAsyncMethod(targetMethod, args);
            }
            else
            {
                // Handle sync methods
                Plugin.TryAcquireOperationLockAsync().Wait();
                Plugin.ReleaseOperationLock();
            }
        }

        return targetMethod.Invoke(_target, args);
    }

    /// <summary>
    /// Handles async method calls with configuration locking.
    /// </summary>
    private async Task<object?> HandleAsyncMethod(MethodInfo method, object?[]? args)
    {
        await Plugin.TryAcquireOperationLockAsync();
        Plugin.ReleaseOperationLock();

        var result = method.Invoke(_target, args);
        
        if (result is Task task)
        {
            await task;
            
            // If it's a Task<T>, return the result
            if (task.GetType().IsGenericType)
            {
                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task);
            }
        }

        return result;
    }
}
