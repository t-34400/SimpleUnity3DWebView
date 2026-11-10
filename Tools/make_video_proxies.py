"""Make silent review proxies and source-time metadata without trimming originals."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
from datetime import datetime


def run(command):
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode:
        raise ValueError(result.stderr[-4000:] or 'Media command failed')
    return result.stdout


def inspect(path, ffprobe):
    data = json.loads(run([ffprobe, '-v', 'error', '-show_streams', '-show_format',
                           '-of', 'json', str(path)]))
    videos = [s for s in data['streams'] if s['codec_type'] == 'video'
              and not s.get('disposition', {}).get('attached_pic')]
    if len(videos) != 1:
        raise ValueError('Expected exactly one video stream: ' + path.name)
    video = videos[0]
    # Use the actual video timeline, not an audio tail or container duration.
    duration = float(video.get('duration', 0))
    if not math.isfinite(duration) or duration <= 0:
        raise ValueError('Video stream duration is unavailable: ' + path.name)
    keys = ('index', 'codec_name', 'width', 'height', 'pix_fmt', 'sample_aspect_ratio',
            'display_aspect_ratio', 'avg_frame_rate', 'r_frame_rate', 'time_base',
            'start_time', 'duration', 'nb_frames')
    summary = {key: video[key] for key in keys if key in video}
    rotations = [s['rotation'] for s in video.get('side_data_list', []) if 'rotation' in s]
    summary['rotation_degrees'] = rotations[0] if rotations else int(video.get('tags', {}).get('rotate', 0))
    summary['duration_seconds'] = duration
    # Exclude arbitrary container tags (e.g. GPS, account names, device IDs).
    summary['has_audio'] = any(s['codec_type'] == 'audio' for s in data['streams'])
    return summary


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(chunk)
    return value.hexdigest()


def make_proxies(sources, output, *, ffmpeg='ffmpeg', ffprobe='ffprobe', fps=15,
                 width=960, height=720, max_bytes=30_000_000):
    if not 1 <= fps <= 60 or width < 2 or height < 2 or max_bytes < 10_000:
        raise ValueError('Invalid frame rate, dimensions or size limit')
    output = Path(output).resolve()
    sources = [Path(s).resolve(strict=True) for s in sources]
    if not sources or len(set(sources)) != len(sources):
        raise ValueError('Provide distinct original files')
    if output.exists():
        raise ValueError('Output directory already exists; choose a new directory')
    if any(output == s or output in s.parents for s in sources):
        raise ValueError('Keep originals outside the proxy output directory')
    details = [inspect(s, ffprobe) for s in sources]
    for source, video in zip(sources, details):
        if video['duration_seconds'] > 120:
            raise ValueError('Use individual takes of at most 120 seconds; no automatic trimming: ' + source.name)
    output.parent.mkdir(parents=True, exist_ok=True)
    manifest = {'schema_version': 1, 'time_unit': 'seconds',
                'time_origin': 'first displayed video frame of each original',
                'mapping': 'original_seconds = source_offset_seconds + proxy_seconds',
                'audio': 'Proxies are silent; originals are unchanged', 'clips': []}
    # Publish the directory only when every proxy and its metadata are complete.
    with tempfile.TemporaryDirectory(prefix='.vrwebview-proxy-', dir=output.parent) as temp:
        staging = Path(temp) / 'review'
        staging.mkdir()
        for index, (source, video) in enumerate(zip(sources, details), 1):
            name = re.sub(r'[^A-Za-z0-9._-]+', '_', source.stem).strip('._') or 'clip'
            target = staging / f'{index:02d}-{name[:64]}.proxy.mp4'
            duration = video['duration_seconds']
            bitrate = min(1_500_000, int(max_bytes * 8 * .82 / duration))
            for attempt in range(3):
                run([ffmpeg, '-v', 'error', '-nostdin', '-y', '-i', str(source),
                     '-map', f'0:{video["index"]}', '-an', '-sn', '-dn',
                     '-map_metadata', '-1', '-map_chapters', '-1',
                     '-vf', f'setpts=PTS-STARTPTS,scale=w=trunc(iw*sar/2)*2:h=ih,setsar=1,'
                            f'scale=w=min(iw\\,{width}):h=min(ih\\,{height}):'
                            f'force_original_aspect_ratio=decrease:force_divisible_by=2,'
                            f'fps={fps},setsar=1',
                     '-c:v', 'libx264', '-preset', 'fast', '-pix_fmt', 'yuv420p',
                     '-b:v', str(bitrate), '-maxrate', str(bitrate), '-bufsize', str(bitrate * 2),
                     '-movflags', '+faststart', str(target)])
                if target.stat().st_size <= max_bytes:
                    break
                bitrate = int(bitrate * max_bytes / target.stat().st_size * .85)
            else:
                raise ValueError('Cannot fit proxy under size limit: ' + source.name)
            proxy = inspect(target, ffprobe)
            tolerance = 1 / fps + .025
            if abs(proxy['duration_seconds'] - duration) > tolerance:
                raise ValueError('Duration changed beyond one proxy frame: ' + source.name)
            manifest['clips'].append({
                'original_file': source.name, 'original_bytes': source.stat().st_size,
                'original_sha256': digest(source), 'original_video': video,
                'proxy_file': target.name, 'proxy_bytes': target.stat().st_size,
                'proxy_sha256': digest(target), 'proxy_video': proxy,
                'source_offset_seconds': 0, 'speed': 1,
                'timing_tolerance_seconds': tolerance})
        (staging / 'manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
        staging.rename(output)
    return manifest


def default_output():
    downloads = Path('/mnt/c/Users/tmiyoshi/Downloads')
    if not downloads.is_dir():
        downloads = Path.home() / 'Downloads'
    return downloads / ('VRWebView-Proxies-' + datetime.now().strftime('%Y%m%d-%H%M%S'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('originals', type=Path, nargs='+')
    parser.add_argument('--output-dir', type=Path, default=None)
    parser.add_argument('--ffmpeg', default='ffmpeg')
    parser.add_argument('--ffprobe', default='ffprobe')
    parser.add_argument('--fps', type=int, default=15)
    args = parser.parse_args()
    if not shutil.which(args.ffmpeg) or not shutil.which(args.ffprobe):
        parser.error('FFmpeg and ffprobe are required; on WSL: sudo apt install ffmpeg')
    try:
        output = args.output_dir or default_output()
        result = make_proxies(args.originals, output, ffmpeg=args.ffmpeg, ffprobe=args.ffprobe, fps=args.fps)
    except (OSError, ValueError) as error:
        parser.exit(1, 'ERROR: ' + str(error) + '\n')
    print(f'Saved {len(result["clips"])} silent proxies and manifest.json: {output}')
    print('Share these proxy MP4 files and manifest.json; keep the originals locally.')


if __name__ == '__main__':
    main()
