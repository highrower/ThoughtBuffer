import argparse
import json
import os
import sys
import wave

import soundfile as sf
import torch
from silero_vad import load_silero_vad, get_speech_timestamps


def merge_segments(segments, padding_sec=0.30, merge_gap_sec=1.20):
    if not segments:
        return []

    padded = []
    for s in segments:
        start = max(0.0, float(s["start"]) - padding_sec)
        end = float(s["end"]) + padding_sec
        padded.append({"start": start, "end": end})

    padded.sort(key=lambda x: x["start"])

    merged = [padded[0]]
    for seg in padded[1:]:
        current = merged[-1]
        if seg["start"] - current["end"] <= merge_gap_sec:
            current["end"] = max(current["end"], seg["end"])
        else:
            merged.append(seg)

    return merged


def cut_wav_segments(input_wav_path, output_wav_path, segments):
    with wave.open(input_wav_path, "rb") as reader:
        params = reader.getparams()
        sample_rate = reader.getframerate()
        sampwidth = reader.getsampwidth()
        nchannels = reader.getnchannels()

        if nchannels != 1:
            raise RuntimeError("Expected mono wav input.")
        if sample_rate != 16000:
            raise RuntimeError("Expected 16kHz wav input.")

        frames = reader.readframes(reader.getnframes())

    bytes_per_frame = sampwidth * nchannels

    with wave.open(output_wav_path, "wb") as writer:
        writer.setparams(params)

        for seg in segments:
            start_frame = int(seg["start"] * sample_rate)
            end_frame = int(seg["end"] * sample_rate)

            start_byte = start_frame * bytes_per_frame
            end_byte = end_frame * bytes_per_frame

            writer.writeframes(frames[start_byte:end_byte])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--threshold", type=float, default=0.5)
    parser.add_argument("--min-silence-ms", type=int, default=500)
    parser.add_argument("--min-speech-ms", type=int, default=250)
    parser.add_argument("--padding-ms", type=int, default=300)
    parser.add_argument("--merge-gap-ms", type=int, default=1200)
    args = parser.parse_args()

    if not os.path.exists(args.input):
        print(json.dumps({"ok": False, "error": f"Input file not found: {args.input}"}))
        sys.exit(1)

    data, sr = sf.read(args.input, dtype="float32", always_2d=False)

    if sr != 16000:
        print(json.dumps({"ok": False, "error": f"Expected 16000 Hz wav, got {sr} Hz"}))
        sys.exit(1)

    if hasattr(data, "ndim") and data.ndim > 1:
        data = data.mean(axis=1)

    wav = torch.from_numpy(data)

    model = load_silero_vad()

    speech = get_speech_timestamps(
        wav,
        model,
        threshold=args.threshold,
        min_silence_duration_ms=args.min_silence_ms,
        min_speech_duration_ms=args.min_speech_ms,
        return_seconds=True,
    )

    merged = merge_segments(
        speech,
        padding_sec=args.padding_ms / 1000.0,
        merge_gap_sec=args.merge_gap_ms / 1000.0,
    )

    if not merged:
        print(json.dumps({"ok": True, "segments": 0, "copied": False, "message": "No speech detected"}))
        sys.exit(0)

    cut_wav_segments(args.input, args.output, merged)

    print(json.dumps({
        "ok": True,
        "segments": len(merged),
        "output": args.output
    }))


if __name__ == "__main__":
    main()