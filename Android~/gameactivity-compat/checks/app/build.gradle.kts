plugins { id("com.android.application") }
android {
    namespace = "com.t34400.vrwebview.gameime.smoke"
    compileSdk = 35
    buildTypes { getByName("release") { isMinifyEnabled = true; proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt")) } }
    ndkVersion = "27.2.12479018"
    defaultConfig { applicationId = "com.t34400.vrwebview.gameime.smoke"; minSdk = 29; targetSdk = 35; ndk { abiFilters += "arm64-v8a" } }
}
dependencies { implementation(project(":")) }
