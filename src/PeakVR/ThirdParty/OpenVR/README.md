# OpenVR (Valve)

C# sources from Valve's OpenVR Unity XR plugin, used by PeakVR's experimental OpenVR backend.

- Source: https://github.com/ValveSoftware/unity-xr-plugin (`com.valve.openvr`, commit `a30a010`)
- License: BSD 3-Clause, see [LICENSE.md](LICENSE.md)

Local changes:

- UTF-8 BOM removed and `#nullable disable` added at the top of each file
- `using UnityEngine.Experimental.XR;` removed
- `InputLayoutLoader.RegisterInputLayouts()` stays disabled; PeakVR feeds buttons through its own device (`OpenVRInput.cs`)

The native `XRSDKOpenVR.dll` and `openvr_api.dll` are not in the repository; see the main README.
