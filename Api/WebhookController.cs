using Jellyfin.Plugin.JellyseerrBridge.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Api;

/// <summary>
/// Webhook API controller for handling Jellyfin webhook events.
/// </summary>
[ApiController]
[Route("Plugins/JellyseerrBridge/Webhook")]
public class WebhookController : ControllerBase
{
    private readonly WebhookHandlerService _webhookHandler;
    private readonly ILogger<WebhookController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookController"/> class.
    /// </summary>
    /// <param name="webhookHandler">The webhook handler service.</param>
    /// <param name="logger">The logger.</param>
    public WebhookController(WebhookHandlerService webhookHandler, ILogger<WebhookController> logger)
    {
        _webhookHandler = webhookHandler;
        _logger = logger;
    }

    /// <summary>
    /// Handles webhook events from Jellyfin.
    /// </summary>
    /// <returns>Success status.</returns>
    [HttpPost]
    public async Task<ActionResult> HandleWebhook()
    {
        try
        {
            _logger.LogDebug("Received webhook request");

            // Read the request body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            if (string.IsNullOrEmpty(body))
            {
                _logger.LogWarning("Received empty webhook body");
                return BadRequest("Empty webhook body");
            }

            _logger.LogDebug("Webhook body: {Body}", body);

            // Parse the JSON
            var webhookData = System.Text.Json.JsonSerializer.Deserialize<object>(body);
            if (webhookData == null)
            {
                _logger.LogWarning("Failed to parse webhook JSON");
                return BadRequest("Invalid webhook JSON");
            }

            // Handle the webhook
            var success = await _webhookHandler.HandleWebhookAsync(webhookData);
            
            if (success)
            {
                _logger.LogDebug("Webhook handled successfully");
                return Ok(new { status = "success" });
            }
            else
            {
                _logger.LogWarning("Webhook handling failed");
                return StatusCode(500, new { status = "error", message = "Webhook handling failed" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook");
            return StatusCode(500, new { status = "error", message = "Internal server error" });
        }
    }

    /// <summary>
    /// Health check endpoint for webhook.
    /// </summary>
    /// <returns>Health status.</returns>
    [HttpGet]
    public ActionResult HealthCheck()
    {
        _logger.LogDebug("Webhook health check requested");
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
