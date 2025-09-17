# Implementation Plan

- [ ] 1. Create dedicated FFmpeg.AutoGen audio resampler
  - Replace current AudioResampler with proper FFmpeg.AutoGen SwrContext implementation
  - Remove dependency on FfmpegVadPreprocessor for resampling
  - _Requirements: 2.1, 2.2, 2.3, 2.4_

- [ ] 2. Remove hardcoded SDP generation in InboundCallHandler
  - Replace GenerateSimpleAnswerSdp with MediaSessionManager.CreateAnswerAsync
  - Ensure proper ICE configuration is used
  - _Requirements: 3.1, 3.2, 3.3_

- [ ] 3. Verify dynamic sample rate handling in AIAutoResponder
  - Ensure AudioData.SampleRate is properly used
  - Test with different TTS sample rates
  - _Requirements: 1.1, 1.2, 1.3_

- [ ] 4. Clean up and test the complete solution
  - Remove unused code and imports
  - Verify no memory leaks or resource issues
  - _Requirements: 4.1, 4.2, 4.3, 4.4_