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

android { testOptions { unitTests.isIncludeAndroidResources = true } }
dependencies {
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.robolectric:robolectric:4.14.1")
}
tasks.withType<Test>().configureEach {
    systemProperty("robolectric.dependency.repo.url", "https://repo.maven.apache.org/maven2")
}
