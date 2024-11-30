# Looking for Samples?

The Meta XR All-in-One SDK itself does not contain samples, as it is a wrapper package that installs other Meta XR SDKs as dependencies. However, some of its dependencies (like Meta XR Core SDK) may contain small samples. To show all packages and import their samples:

1. If using Unity Editor 2021, first navigate to **Edit** -> **Project Settings** -> **Package Manager**, and make sure **Show Dependencies** is checked.
2. Navigate to **Window** > **Package Manager**.
3. In the top bar of the window, switch the dropdown from **Packages: My Assets** to **Packages: In Project**.
4. Click on an installed package (marked with a green check) to show package details.
5. If the installed package has samples, a "Samples" section will appear either under the package description or as a section tab, depending on the Unity Editor version.
6. Go to the samples section, and click import to add the samples into your project.

Larger samples can be found on Github: https://github.com/oculus-samples. A good starting point for samples of common features, including samples from the legacy Oculus Integrations SDK, is the Unity-StarterSamples repository: https://developer.oculus.com/documentation/unity/unity-starter-samples/

Additionally, the following SDKs have separate UPM packages for their samples:
* Interaction SDK Samples: https://developer.oculus.com/downloads/package/meta-xr-interaction-sdk-ovr-samples
* Meta XR Simulator Samples: https://developer.oculus.com/downloads/package/meta-xr-simulator-samples

Still having trouble? See documentation on samples for Meta XR SDKs (https://developer.oculus.com/documentation/unity/unity-import-samples/), or reach out to us via the Meta Quest Developer Forums: https://communityforums.atmeta.com/t5/Developer/ct-p/developer
