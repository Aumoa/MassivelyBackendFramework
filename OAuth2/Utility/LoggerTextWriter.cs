using System.Text;

namespace OAuth2.Utility
{
    public class LoggerTextWriter : TextWriter
    {
        private readonly ILogger _logger;
        private readonly LogLevel _logLevel;

        public LoggerTextWriter(ILogger logger, LogLevel logLevel = LogLevel.Information)
        {
            ArgumentNullException.ThrowIfNull(logger);
            _logger = logger;
            _logLevel = logLevel;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            _logger.Log(_logLevel, "{value}", value);
        }

        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _logger.Log(_logLevel, "{value}", value);
            }
        }

        public override void WriteLine(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _logger.Log(_logLevel, "{value}", value);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            var str = new string(buffer, index, count);
            _logger.Log(_logLevel, "{value}", str);
        }

        public override void WriteLine()
        {
        }
    }
}