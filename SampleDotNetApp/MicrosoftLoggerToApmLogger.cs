using Elastic.Apm.Logging;
using Microsoft.Extensions.Logging;
using System;

namespace SampleDotNetApp
{
    public class MicrosoftLoggerToApmLogger : IApmLogger
    {
        private readonly ILogger _logger;

        public MicrosoftLoggerToApmLogger(ILogger logger)
        {
            _logger = logger;
        }

        public bool IsEnabled(Elastic.Apm.Logging.LogLevel level) => _logger.IsEnabled(ConvertLevel(level));

        public void Log<TState>(Elastic.Apm.Logging.LogLevel level, TState state, Exception e, Func<TState, Exception, string> formatter)
        {
            _logger.Log(ConvertLevel(level), e, formatter(state, e));
        }

        private static Microsoft.Extensions.Logging.LogLevel ConvertLevel(Elastic.Apm.Logging.LogLevel level)
        {
            return level switch
            {
                Elastic.Apm.Logging.LogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
                Elastic.Apm.Logging.LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
                Elastic.Apm.Logging.LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
                Elastic.Apm.Logging.LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
                Elastic.Apm.Logging.LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
                Elastic.Apm.Logging.LogLevel.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
                _ => Microsoft.Extensions.Logging.LogLevel.None,
            };
        }
    }
}
