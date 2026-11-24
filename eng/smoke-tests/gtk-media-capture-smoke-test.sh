#!/usr/bin/env bash
set -euo pipefail

work_dir="${1:-artifacts/TestResults/gtk-media-capture}"
mkdir -p "$work_dir"
photo="$work_dir/capture.jpg"
video="$work_dir/capture.webm"
rm -f "$photo" "$video"

for plugin in videotestsrc videoconvert jpegenc jpegdec vp8enc vp8dec webmmux matroskademux; do
  gst-inspect-1.0 "$plugin" >/dev/null
done

gst-launch-1.0 -q \
  videotestsrc num-buffers=1 pattern=smpte \
  ! videoconvert \
  ! jpegenc \
  ! filesink "location=$photo"

gst-launch-1.0 -q -e \
  videotestsrc num-buffers=30 pattern=ball \
  ! videoconvert \
  ! vp8enc deadline=1 \
  ! webmmux \
  ! filesink "location=$video"

test -s "$photo"
test -s "$video"

gst-launch-1.0 -q filesrc "location=$photo" ! jpegdec ! fakesink
gst-launch-1.0 -q filesrc "location=$video" ! matroskademux ! vp8dec ! fakesink

if [[ -e /dev/video0 ]]; then
  camera_photo="$work_dir/camera.jpg"
  gst-launch-1.0 -q \
    v4l2src device=/dev/video0 num-buffers=1 \
    ! videoconvert \
    ! jpegenc \
    ! filesink "location=$camera_photo"
  test -s "$camera_photo"
else
  printf '%s\n' "No /dev/video0 camera is present; deterministic codec/container validation passed." \
    > "$work_dir/native-camera-skipped.txt"
fi
