using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Concentus.Enums;
using Concentus.Structs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UltraVoice.Client.Services.Audio;
using UltraVoice.Shared.Configuration;
using UltraVoice.Shared.Audio;

namespace UltraVoice.Client.Services;

/// <summary>
/// Handles capture, encode and playback for the UltraVoice client.
/// </summary>
public sealed class AudioEngine : IDisposable, IAudioSink
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int FrameDurationMs = 20;
    public const int SamplesPerFrame = SampleRate * FrameDurationMs / 1000;
    public const int BytesPerSample = 2;
    public const int FrameSizeBytes = SamplesPerFrame * BytesPerSample;

    private readonly AppState _state;
    private readonly ClientTransport _transport;
    private readonly WaveInEvent _waveIn;
    private readonly WaveOutEvent _waveOut;
    private readonly MixingSampleProvider _mixer;
    private readonly ConcurrentDictionary<uint, RemoteStream> _remoteStreams = new();
    private readonly HashSet<uint> _activeSpeakers = new();
    private readonly OpusEncoder _encoder;
    private readonly object _captureGate = new();
    private readonly object _playbackGate = new();
    private readonly short[] _pcmBuffer = new short[SamplesPerFrame];
    private readonly byte[] _opusBuffer = new byte[400];
    private readonly List<byte> _captureRemainder = new();
    private bool _isStarted;
    private double _inputGainDb;
    private ushort _sequence;
    private IDisposable? _activeSpeakerSubscription;

    public AudioEngine(AppState state, ClientTransport transport)
    {
        _state = state;
        _transport = transport;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, Channels),
            BufferMilliseconds = FrameDurationMs
        };
        _waveIn.DataAvailable += OnCaptureData;

        _waveOut = new WaveOutEvent();
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
        {
            ReadFully = true
        };
        _waveOut.Init(_mixer);
        _waveOut.Play();

        _encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = 24000;
        _encoder.Complexity = 2;
        _encoder.UseInbandFEC = false;
        _encoder.UseDTX = true;

        _activeSpeakerSubscription = _state.ActiveSpeakers.Subscribe(UpdateActiveSpeakers);
        _transport.RegisterAudioSink(this);
    }

    public IReadOnlyList<string> GetInputDevices() =>
        new[] { "Varsayılan Giriş" };

    public IReadOnlyList<string> GetOutputDevices() =>
        new[] { "Varsayılan Çıkış" };

    public void SelectInputDevice(string? deviceId)
    {
        _waveIn.DeviceNumber = 0;
    }

    public void SelectOutputDevice(string? deviceId)
    {
        _waveOut.DeviceNumber = 0;
    }

    public void SetInputGain(double inputGainDb)
    {
        _inputGainDb = inputGainDb;
    }

    public void StartCapture()
    {
        if (_isStarted)
        {
            return;
        }

        _sequence = 0;
        _captureRemainder.Clear();
        _waveIn.StartRecording();
        _isStarted = true;
    }

    public void StopCapture()
    {
        if (!_isStarted)
        {
            return;
        }

        _waveIn.StopRecording();
        _captureRemainder.Clear();
        _isStarted = false;
    }

    private void OnCaptureData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
        {
            return;
        }

        lock (_captureGate)
        {
            if (_captureRemainder.Count > 0)
            {
                _captureRemainder.AddRange(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());
            }
            else
            {
                _captureRemainder.AddRange(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());
            }

            var span = _captureRemainder.ToArray();
            var offset = 0;
            while (offset + FrameSizeBytes <= span.Length)
            {
                var frameSpan = new ReadOnlySpan<byte>(span, offset, FrameSizeBytes);
                EncodeAndSendFrame(frameSpan);
                offset += FrameSizeBytes;
            }

            _captureRemainder.RemoveRange(0, offset);
        }
    }

    private void EncodeAndSendFrame(ReadOnlySpan<byte> frameSpan)
    {
        for (var i = 0; i < SamplesPerFrame; i++)
        {
            var sample = BitConverter.ToInt16(frameSpan.Slice(i * 2, 2));
            if (Math.Abs(_inputGainDb) > 0.001)
            {
                var gain = Math.Pow(10, _inputGainDb / 20d);
                sample = (short)Math.Clamp(sample * gain, short.MinValue, short.MaxValue);
            }

            _pcmBuffer[i] = sample;
        }

        var rms = AudioMath.CalculateRms(_pcmBuffer);

        var encodedLength = _encoder.Encode(_pcmBuffer, 0, SamplesPerFrame, _opusBuffer, 0, _opusBuffer.Length);
        if (encodedLength <= 0)
        {
            return;
        }

        var payload = new byte[encodedLength];
        Buffer.BlockCopy(_opusBuffer, 0, payload, 0, encodedLength);

        var message = new AudioFrameMessage
        {
            Sequence = _sequence++,
            CaptureTimestampMs = (uint)Environment.TickCount64,
            Rms = rms,
            Payload = payload
        };

        _transport.SendAudioFrame(message);
    }

    public void Dispose()
    {
        StopCapture();
        _activeSpeakerSubscription?.Dispose();
        foreach (var stream in _remoteStreams.Values)
        {
            stream.Dispose();
        }
        _remoteStreams.Clear();
        _waveIn.Dispose();
        _waveOut.Dispose();
    }

    private void UpdateActiveSpeakers(IReadOnlyCollection<uint> speakers)
    {
        lock (_playbackGate)
        {
            _activeSpeakers.Clear();
            foreach (var speaker in speakers)
            {
                _activeSpeakers.Add(speaker);
            }

            foreach (var kvp in _remoteStreams)
            {
                if (!_activeSpeakers.Contains(kvp.Key))
                {
                    kvp.Value.Clear();
                }
            }
        }
    }

    public void HandleAudio(uint sessionId, AudioFrameMessage frame)
    {
        if (frame.Payload.Length == 0)
        {
            return;
        }

        var stream = _remoteStreams.GetOrAdd(sessionId, CreateRemoteStream);
        stream.LastRms = frame.Rms;
        stream.LastFrameAt = DateTime.UtcNow;
        _state.UpdateUserLevel(sessionId, frame.Rms);

        bool shouldDecode;
        lock (_playbackGate)
        {
            shouldDecode = _activeSpeakers.Count == 0 || _activeSpeakers.Contains(sessionId);
        }

        if (!shouldDecode)
        {
            return;
        }

        var decoded = stream.Decoder.Decode(frame.Payload, 0, frame.Payload.Length, stream.PcmBuffer, 0, SamplesPerFrame, false);
        if (decoded <= 0)
        {
            return;
        }

        var byteCount = decoded * BytesPerSample;
        stream.EnsureByteBuffer(byteCount);
        Buffer.BlockCopy(stream.PcmBuffer, 0, stream.ByteBuffer, 0, byteCount);
        stream.Buffer.AddSamples(stream.ByteBuffer, 0, byteCount);
    }

    private RemoteStream CreateRemoteStream(uint sessionId)
    {
        var decoder = new OpusDecoder(SampleRate, Channels);
        var buffer = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, Channels))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(200)
        };
        var sampleProvider = buffer.ToSampleProvider();
        _mixer.AddMixerInput(sampleProvider);

        return new RemoteStream(decoder, buffer);
    }

    private sealed class RemoteStream : IDisposable
    {
        public RemoteStream(OpusDecoder decoder, BufferedWaveProvider buffer)
        {
            Decoder = decoder;
            Buffer = buffer;
            PcmBuffer = new short[AudioEngine.SamplesPerFrame];
        }

        public OpusDecoder Decoder { get; }
        public BufferedWaveProvider Buffer { get; }
        public short[] PcmBuffer { get; }
        public byte[] ByteBuffer { get; private set; } = new byte[AudioEngine.FrameSizeBytes];
        public float LastRms { get; set; }
        public DateTime LastFrameAt { get; set; }

        public void EnsureByteBuffer(int byteCount)
        {
            if (ByteBuffer.Length < byteCount)
            {
                ByteBuffer = new byte[byteCount];
            }
        }

        public void Clear() => Buffer.ClearBuffer();

        public void Dispose()
        {
            Buffer.ClearBuffer();
        }
    }
}
