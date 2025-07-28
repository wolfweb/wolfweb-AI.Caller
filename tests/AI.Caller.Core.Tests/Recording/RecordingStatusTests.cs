using AI.Caller.Core.Recording;
using Xunit;

namespace AI.Caller.Core.Tests.Recording
{
    public class RecordingStatusTests
    {
        [Fact]
        public void RecordingStatus_Constructor_ShouldSetDefaultValues()
        {
            // Arrange & Act
            var status = new RecordingStatus();
            
            // Assert
            Assert.Equal(RecordingState.Idle, status.State);
            Assert.Null(status.StartTime);
            Assert.Null(status.EndTime);
            Assert.Equal(TimeSpan.Zero, status.Duration);
            Assert.Null(status.CurrentFilePath);
            Assert.Equal(0, status.BytesRecorded);
            Assert.Null(status.ErrorMessage);
            Assert.Null(status.ErrorCode);
            Assert.Null(status.AudioFormat);
            Assert.Null(status.Options);
            Assert.True(status.LastUpdated <= DateTime.UtcNow);
            Assert.Equal(0.0, status.AudioLevel);
            Assert.False(status.IsRecording);
            Assert.False(status.IsPaused);
            Assert.False(status.IsCompleted);
            Assert.False(status.HasError);
            Assert.True(status.CanStart);
            Assert.False(status.CanStop);
            Assert.False(status.CanPause);
            Assert.False(status.CanResume);
        }
        
        [Fact]
        public void UpdateState_ToRecording_ShouldSetStartTime()
        {
            // Arrange
            var status = new RecordingStatus();
            var beforeUpdate = DateTime.UtcNow;
            
            // Act
            status.UpdateState(RecordingState.Recording);
            
            // Assert
            Assert.Equal(RecordingState.Recording, status.State);
            Assert.NotNull(status.StartTime);
            Assert.True(status.StartTime >= beforeUpdate);
            Assert.True(status.StartTime <= DateTime.UtcNow);
            Assert.Null(status.EndTime);
            Assert.True(status.IsRecording);
            Assert.True(status.CanStop);
            Assert.True(status.CanPause);
            Assert.False(status.CanStart);
        }
        
        [Theory]
        [InlineData(RecordingState.Completed)]
        [InlineData(RecordingState.Cancelled)]
        [InlineData(RecordingState.Error)]
        public void UpdateState_ToFinalState_ShouldSetEndTime(RecordingState finalState)
        {
            // Arrange
            var status = new RecordingStatus();
            status.UpdateState(RecordingState.Recording);
            var beforeUpdate = DateTime.UtcNow;
            
            // Act
            status.UpdateState(finalState);
            
            // Assert
            Assert.Equal(finalState, status.State);
            Assert.NotNull(status.EndTime);
            Assert.True(status.EndTime >= beforeUpdate);
            Assert.True(status.EndTime <= DateTime.UtcNow);
            Assert.False(status.CanStop);
            Assert.False(status.CanPause);
            Assert.False(status.CanResume);
        }
        
        [Fact]
        public void UpdateState_ToPaused_ShouldAllowResume()
        {
            // Arrange
            var status = new RecordingStatus();
            status.UpdateState(RecordingState.Recording);
            
            // Act
            status.UpdateState(RecordingState.Paused);
            
            // Assert
            Assert.Equal(RecordingState.Paused, status.State);
            Assert.True(status.IsPaused);
            Assert.True(status.CanResume);
            Assert.True(status.CanStop);
            Assert.False(status.CanPause);
        }
        
        [Fact]
        public void UpdateState_WithMessage_ShouldSetErrorMessage()
        {
            // Arrange
            var status = new RecordingStatus();
            var message = "Test message";
            
            // Act
            status.UpdateState(RecordingState.Recording, message);
            
            // Assert
            Assert.Equal(message, status.ErrorMessage);
        }
        
        [Fact]
        public void SetError_ShouldSetErrorStateAndDetails()
        {
            // Arrange
            var status = new RecordingStatus();
            status.UpdateState(RecordingState.Recording);
            var errorCode = RecordingErrorCode.EncodingFailed;
            var errorMessage = "Encoding failed";
            var beforeError = DateTime.UtcNow;
            
            // Act
            status.SetError(errorCode, errorMessage);
            
            // Assert
            Assert.Equal(RecordingState.Error, status.State);
            Assert.Equal(errorCode, status.ErrorCode);
            Assert.Equal(errorMessage, status.ErrorMessage);
            Assert.True(status.HasError);
            Assert.NotNull(status.EndTime);
            Assert.True(status.EndTime >= beforeError);
        }
        
        [Fact]
        public void ClearError_ShouldClearErrorDetails()
        {
            // Arrange
            var status = new RecordingStatus();
            status.SetError(RecordingErrorCode.EncodingFailed, "Test error");
            
            // Act
            status.ClearError();
            
            // Assert
            Assert.Null(status.ErrorCode);
            Assert.Null(status.ErrorMessage);
        }
        
        [Fact]
        public void Duration_WithStartTime_ShouldCalculateCorrectly()
        {
            // Arrange
            var status = new RecordingStatus();
            var startTime = DateTime.UtcNow.AddMinutes(-5);
            status.StartTime = startTime;
            
            // Act
            var duration = status.Duration;
            
            // Assert
            Assert.True(duration.TotalMinutes >= 4.9);
            Assert.True(duration.TotalMinutes <= 5.1);
        }
        
        [Fact]
        public void Duration_WithStartAndEndTime_ShouldCalculateCorrectly()
        {
            // Arrange
            var status = new RecordingStatus();
            var startTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var endTime = new DateTime(2024, 1, 1, 10, 5, 0, DateTimeKind.Utc);
            status.StartTime = startTime;
            status.EndTime = endTime;
            
            // Act
            var duration = status.Duration;
            
            // Assert
            Assert.Equal(TimeSpan.FromMinutes(5), duration);
        }
        
        [Fact]
        public void Clone_ShouldCreateExactCopy()
        {
            // Arrange
            var original = new RecordingStatus();
            original.UpdateState(RecordingState.Recording);
            original.CurrentFilePath = "/test/path";
            original.BytesRecorded = 1024;
            original.AudioLevel = 0.5;
            original.SetError(RecordingErrorCode.EncodingFailed, "Test error");
            
            // Act
            var clone = original.Clone();
            
            // Assert
            Assert.Equal(original.State, clone.State);
            Assert.Equal(original.StartTime, clone.StartTime);
            Assert.Equal(original.EndTime, clone.EndTime);
            Assert.Equal(original.CurrentFilePath, clone.CurrentFilePath);
            Assert.Equal(original.BytesRecorded, clone.BytesRecorded);
            Assert.Equal(original.ErrorMessage, clone.ErrorMessage);
            Assert.Equal(original.ErrorCode, clone.ErrorCode);
            Assert.Equal(original.AudioFormat, clone.AudioFormat);
            Assert.Equal(original.Options, clone.Options);
            Assert.Equal(original.LastUpdated, clone.LastUpdated);
            Assert.Equal(original.AudioLevel, clone.AudioLevel);
            
            // Ensure it's a different instance
            Assert.NotSame(original, clone);
        }
        
        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            // Arrange
            var status = new RecordingStatus();
            status.UpdateState(RecordingState.Recording);
            status.BytesRecorded = 1024;
            
            // Act
            var result = status.ToString();
            
            // Assert
            Assert.Contains("State: Recording", result);
            Assert.Contains("Bytes: 1,024", result);
            Assert.Contains("Duration:", result);
        }
    }
}