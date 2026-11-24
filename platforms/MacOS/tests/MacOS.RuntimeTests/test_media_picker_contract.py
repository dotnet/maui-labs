import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]


class MediaPickerContractTests(unittest.TestCase):
    def test_photo_capture_uses_imagekit_and_explicit_privacy_contract(self):
        source = (
            ROOT / "src" / "MacOS.Essentials" / "MediaPickerImplementation.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("IKPictureTaker.SharedPictureTaker", source)
        self.assertIn("NSCameraUsageDescription", source)
        self.assertIn("AVCaptureDevice.GetDefaultDevice", source)
        self.assertIn("NSBitmapImageFileType.Png", source)
        self.assertIn("completion.TrySetResult(null)", source)
        self.assertIn("EnsureCameraPermissionAsync", source)

    def test_video_capture_uses_avfoundation_and_cleans_partial_output(self):
        source = (
            ROOT / "src" / "MacOS.Essentials" / "MediaPickerImplementation.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("AVCaptureSession", source)
        self.assertIn("AVCaptureDeviceInput.FromDevice", source)
        self.assertIn("AVCaptureMovieFileOutput", source)
        self.assertIn("StartRecordingToOutputFile", source)
        self.assertIn("AVCaptureFileOutputRecordingDelegate", source)
        self.assertIn("output.StopRecording()", source)
        self.assertIn("await recordingDelegate.Completion.WaitAsync", source)
        self.assertIn("AVErrorKeys.RecordingSuccessfullyFinished", source)
        self.assertIn("DeleteIfExists(outputPath)", source)
        self.assertIn("new FileInfo(outputPath).Length == 0", source)

    def test_camera_permission_is_explicitly_requested_and_denial_is_not_success(self):
        source = (
            ROOT / "src" / "MacOS.Essentials" / "MediaPickerImplementation.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("GetAuthorizationStatus", source)
        self.assertIn("RequestAccessForMediaTypeAsync", source)
        self.assertIn("AVAuthorizationStatus.Denied", source)
        self.assertIn("AVAuthorizationStatus.Restricted", source)
        self.assertIn("throw new PermissionException", source)

    def test_template_declares_camera_usage_description(self):
        project = (
            ROOT / "templates" / "maui-macos-app" / "MauiMacOSApp.csproj"
        ).read_text(encoding="utf-8")
        manifest = (
            ROOT / "templates" / "maui-macos-app" / "Info.plist"
        ).read_text(encoding="utf-8")

        self.assertIn("<ApplicationManifest>Info.plist</ApplicationManifest>", project)
        self.assertIn("<key>NSCameraUsageDescription</key>", manifest)
        self.assertIn("<string>", manifest)


if __name__ == "__main__":
    unittest.main()
