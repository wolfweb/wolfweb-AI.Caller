using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using AI.Caller.Core.Services;
using AI.Caller.Core.CallAutomation;

namespace AI.Caller.Core.Tests
{
    public class DtmfMappingTests
    {
        [Fact]
        public async Task StartCollectionWithConfigAsync_ShouldMapKeys()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<DtmfService>>();
            var loggerFactoryMock = new Mock<ILoggerFactory>();
            var collectorLoggerMock = new Mock<ILogger<DtmfCollector>>();
            
            loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
                .Returns(collectorLoggerMock.Object);
            
            var service = new DtmfService(loggerMock.Object, loggerFactoryMock.Object);
            var callId = "test_call_1";
            var config = new DtmfCollectionConfig
            {
                InputMapping = new Dictionary<char, char> { { '*', 'X' } },
                MaxLength = 5,
                BackspaceKey = ' ' // Disable backspace conflict if default was *
            };

            // Act
            var collectionTask = service.StartCollectionWithConfigAsync(callId, config);

            // Simulate inputs: 1, 2, 3, *, #
            // * should be mapped to X
            // # is termination (default)
            
            // Wait a bit for async task to register collector
            await Task.Delay(100);

            service.OnDtmfToneReceived(callId, 1); // Tone 1 -> '1'
            service.OnDtmfToneReceived(callId, 2); // Tone 2 -> '2'
            service.OnDtmfToneReceived(callId, 3); // Tone 3 -> '3'
            service.OnDtmfToneReceived(callId, 10); // Tone 10 -> '*' -> mapped to 'X'
            service.OnDtmfToneReceived(callId, 11); // Tone 11 -> '#' -> termination

            var result = await collectionTask;

            Assert.Equal("123X", result);
        }
    }
}
