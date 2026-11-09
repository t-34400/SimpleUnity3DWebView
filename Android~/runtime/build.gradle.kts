plugins { id("com.android.library") }
android {
    namespace = "com.t34400.vrwebview.runtime"
    compileSdk = 35
    defaultConfig { minSdk = 29; consumerProguardFiles("consumer-rules.pro") }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    buildTypes { release { isMinifyEnabled = false } }
}
