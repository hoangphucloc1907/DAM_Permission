using Confluent.Kafka;
using DAM.Models;
using System.Text.Json;

namespace DAM.Services
{
    public class ConsumerService : BackgroundService
    {
        private readonly IConsumer<string, string> _consumer;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ConsumerService> _logger;
        private readonly string _topic;

        public ConsumerService(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<ConsumerService> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            _topic = configuration["Kafka:Topic"] ?? throw new ArgumentException("Kafka topic configuration is missing", nameof(configuration));
            var config = new ConsumerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                GroupId = configuration["Kafka:EmailConsumerGroupId"],
                AutoOffsetReset = AutoOffsetReset.Earliest,
                SecurityProtocol = SecurityProtocol.SaslSsl,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = configuration["Kafka:SaslUsername"],
                SaslPassword = configuration["Kafka:SaslPassword"],
                EnableAutoCommit = false
            };
            _consumer = new ConsumerBuilder<string, string>(config).Build();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            _consumer.Subscribe(_topic);
            _logger.LogInformation($"Started listening on topic: {_topic}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult != null)
                    {
                        await ProcessMessageAsync(consumeResult.Message.Value);
                        _consumer.Commit(consumeResult);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError($"Consume error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Unexpected error: {ex.Message}");
                }
            }
        }

        private async Task ProcessMessageAsync(string message)
        {
            try
            {
                var eventData = JsonSerializer.Deserialize<JsonElement>(message);

                // Safely check if EventType property exists
                if (!eventData.TryGetProperty("EventType", out var eventTypeElement))
                {
                    _logger.LogWarning("Message is missing EventType property: {Message}", message);
                    return;
                }

                var eventType = eventTypeElement.GetString();
                if (string.IsNullOrEmpty(eventType))
                {
                    _logger.LogWarning("EventType is null or empty: {Message}", message);
                    return;
                }

                switch (eventType)
                {
                    case "ResourceShared":
                        await HandleResourceSharedEventAsync(eventData);
                        break;
                    case "PublicLinkGenerated":
                        await HandlePublicLinkGeneratedEventAsync(eventData);
                        break;
                    case "AccessRequested":
                        await HandleAccessRequestedEventAsync(eventData);
                        break;
                    case "AccessRequestApproved":
                        await HandleAccessRequestApprovedEventAsync(eventData);
                        break;
                    case "AccessRequestDenied":
                        await HandleAccessRequestDeniedEventAsync(eventData);
                        break;
                    default:
                        _logger.LogWarning($"Unknown event type: {eventType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing message: {ex.Message}. Message content: {message}");
            }
        }

        private async Task HandleResourceSharedEventAsync(JsonElement eventData)
        {
            try
            {
                // Safe property access
                if (!TryGetStringProperty(eventData, "RecipientEmail", out var recipientEmail) ||
                    !TryGetStringProperty(eventData, "ResourceName", out var resourceName) ||
                    !TryGetStringProperty(eventData, "ResourceType", out var resourceType) ||
                    !TryGetStringProperty(eventData, "PermissionType", out var permissionType))
                {
                    _logger.LogWarning("Missing required properties for ResourceShared event");
                    return;
                }

                if (string.IsNullOrEmpty(recipientEmail))
                {
                    _logger.LogWarning("RecipientEmail is empty for ResourceShared event");
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                var subject = $"New {permissionType} Permission Granted";
                var content = $"You have been granted {permissionType} access to the {resourceType.ToLower()} '{resourceName}'.";

                await emailService.SendEmail(recipientEmail, content, subject);
                _logger.LogInformation($"Sent resource sharing notification to {recipientEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HandleResourceSharedEventAsync: {ex.Message}");
            }
        }

        private async Task HandlePublicLinkGeneratedEventAsync(JsonElement eventData)
        {
            try
            {
                // Safe property access
                if (!TryGetStringProperty(eventData, "OwnerEmail", out var ownerEmail) ||
                    !TryGetStringProperty(eventData, "ResourceName", out var resourceName) ||
                    !TryGetStringProperty(eventData, "ResourceType", out var resourceType) ||
                    !TryGetStringProperty(eventData, "ShareToken", out var shareToken) ||
                    !eventData.TryGetProperty("ExpiresAt", out var expiresAtElement))
                {
                    _logger.LogWarning("Missing required properties for PublicLinkGenerated event");
                    return;
                }

                if (string.IsNullOrEmpty(ownerEmail))
                {
                    _logger.LogWarning("OwnerEmail is empty for PublicLinkGenerated event");
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                var expiresAt = expiresAtElement.TryGetDateTime(out var dateTime) ? dateTime : DateTime.UtcNow.AddDays(7);
                var sharingLink = $"https://yourdomain.com/share/{shareToken}";
                var subject = $"Public Link Created for {resourceName}";
                var content = $"You've created a public sharing link for {resourceType.ToLower()} '{resourceName}'.\n\n" +
                              $"Link: {sharingLink}\n" +
                              $"This link will expire on {expiresAt:g}.\n\n" +
                              $"Anyone with this link can access your {resourceType.ToLower()} with the specified permissions.";

                await emailService.SendEmail(ownerEmail, content, subject);
                _logger.LogInformation($"Sent public link notification to owner {ownerEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HandlePublicLinkGeneratedEventAsync: {ex.Message}");
            }
        }

        private async Task HandleAccessRequestedEventAsync(JsonElement eventData)
        {
            try
            {
                // Safe property access
                if (!TryGetStringProperty(eventData, "OwnerEmail", out var ownerEmail) ||
                    !TryGetStringProperty(eventData, "RequesterEmail", out var requesterEmail) ||
                    !TryGetStringProperty(eventData, "ResourceName", out var resourceName) ||
                    !TryGetStringProperty(eventData, "ResourceType", out var resourceType) ||
                    !TryGetStringProperty(eventData, "RequestedPermissionType", out var requestedPermission) ||
                    !eventData.TryGetProperty("RequestId", out var requestIdElement) ||
                    !requestIdElement.TryGetInt32(out var requestId))
                {
                    _logger.LogWarning("Missing required properties for AccessRequested event");
                    return;
                }

                if (string.IsNullOrEmpty(ownerEmail))
                {
                    _logger.LogWarning("OwnerEmail is empty for AccessRequested event");
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                var approvalUrl = $"https://localhost:7197/api/AccessRequest/{requestId}/approve";
                var denyUrl = $"https://localhost:7197/api/AccessRequest/{requestId}/deny";

                var subject = $"Access Request for {resourceName}";
                var content = $"{requesterEmail} has requested {requestedPermission} access to your {resourceType.ToLower()} '{resourceName}'.\n\n" +
                              $"To approve this request, click here: {approvalUrl}\n" +
                              $"To deny this request, click here: {denyUrl}\n\n" +
                              $"You can also manage all access requests from your dashboard.";

                await emailService.SendEmail(ownerEmail, content, subject);
                _logger.LogInformation($"Sent access request notification to owner {ownerEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HandleAccessRequestedEventAsync: {ex.Message}");
            }
        }

        private async Task HandleAccessRequestApprovedEventAsync(JsonElement eventData)
        {
            try
            {
                // Safe property access
                if (!TryGetStringProperty(eventData, "RequesterEmail", out var requesterEmail) ||
                    !TryGetStringProperty(eventData, "ResourceName", out var resourceName) ||
                    !TryGetStringProperty(eventData, "ResourceType", out var resourceType) ||
                    !TryGetStringProperty(eventData, "GrantedPermissionType", out var grantedPermission) ||
                    !eventData.TryGetProperty("ResourceId", out var resourceIdElement) ||
                    !resourceIdElement.TryGetInt32(out var resourceId))
                {
                    _logger.LogWarning("Missing required properties for AccessRequestApproved event");
                    return;
                }

                if (string.IsNullOrEmpty(requesterEmail))
                {
                    _logger.LogWarning("RequesterEmail is empty for AccessRequestApproved event");
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                var resourceUrl = $"https://localhost:7197/{resourceType.ToLower()}/{resourceId}";

                var subject = $"Access Request Approved for {resourceName}";
                var content = $"Good news! Your request for {grantedPermission} access to the {resourceType.ToLower()} '{resourceName}' has been approved.\n\n" +
                              $"You can access it here: {resourceUrl}";

                await emailService.SendEmail(requesterEmail, content, subject);
                _logger.LogInformation($"Sent approval notification to requester {requesterEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HandleAccessRequestApprovedEventAsync: {ex.Message}");
            }
        }

        private async Task HandleAccessRequestDeniedEventAsync(JsonElement eventData)
        {
            try
            {
                // Safe property access
                if (!TryGetStringProperty(eventData, "RequesterEmail", out var requesterEmail) ||
                    !TryGetStringProperty(eventData, "ResourceName", out var resourceName) ||
                    !TryGetStringProperty(eventData, "ResourceType", out var resourceType))
                {
                    _logger.LogWarning("Missing required properties for AccessRequestDenied event");
                    return;
                }

                if (string.IsNullOrEmpty(requesterEmail))
                {
                    _logger.LogWarning("RequesterEmail is empty for AccessRequestDenied event");
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                var denialReason = "No reason provided";
                if (eventData.TryGetProperty("DenialReason", out var reasonElement) &&
                    reasonElement.ValueKind == JsonValueKind.String)
                {
                    denialReason = reasonElement.GetString() ?? denialReason;
                }

                var subject = $"Access Request Denied for {resourceName}";
                var content = $"Your request for access to the {resourceType.ToLower()} '{resourceName}' has been denied.\n\n";

                if (!string.IsNullOrEmpty(denialReason))
                {
                    content += $"Reason: {denialReason}\n\n";
                }

                content += "If you believe this is in error, please contact the resource owner directly.";

                await emailService.SendEmail(requesterEmail, content, subject);
                _logger.LogInformation($"Sent denial notification to requester {requesterEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HandleAccessRequestDeniedEventAsync: {ex.Message}");
            }
        }

        // Helper method to safely get string property
        private bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
        {
            value = string.Empty;

            if (!element.TryGetProperty(propertyName, out var property))
            {
                _logger.LogWarning($"Property '{propertyName}' not found in event data");
                return false;
            }

            if (property.ValueKind != JsonValueKind.String)
            {
                _logger.LogWarning($"Property '{propertyName}' is not a string");
                return false;
            }

            value = property.GetString() ?? string.Empty;
            return true;
        }

        public override void Dispose()
        {
            _consumer?.Close();
            _consumer?.Dispose();
            base.Dispose();
        }
    }

    
}
