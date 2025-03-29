using Confluent.Kafka;

namespace DAM.Services
{
    public interface IProducerService
    {
        Task ProduceAsync(string topic, string message);
    }
    public class ProducerService : IProducerService, IDisposable
    {
        private readonly IProducer<string, string> _producer;
        private readonly ILogger<ProducerService> _logger;
        private bool _disposed = false;

        public ProducerService(IConfiguration configuration, ILogger<ProducerService> logger)
        {
            _logger = logger;
            var config = new ProducerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                SecurityProtocol = SecurityProtocol.SaslSsl,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = configuration["Kafka:SaslUsername"],
                SaslPassword = configuration["Kafka:SaslPassword"],
                Acks = Acks.All,
                EnableIdempotence = true,
                MessageSendMaxRetries = 3,
                RetryBackoffMs = 1000
            };
            _producer = new ProducerBuilder<string, string>(config).Build();
        }


        public async Task ProduceAsync(string topic, string message)
        {
            if (string.IsNullOrEmpty(topic))
                throw new ArgumentException("Topic cannot be null or empty", nameof(topic));

            if (string.IsNullOrEmpty(message))
                throw new ArgumentException("Message cannot be null or empty", nameof(message));

            try
            {
                // Use message timestamp as the key to ensure ordering
                var messageKey = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

                var result = await _producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = messageKey,
                    Value = message,
                    Timestamp = new Timestamp(DateTime.UtcNow)
                });

                _logger.LogInformation($"Message delivered to '{topic}' at '{result.TopicPartitionOffset}'");
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError($"Failed to deliver message to '{topic}': {ex.Message}");
                throw;
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _producer?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
