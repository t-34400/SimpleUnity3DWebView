plugins {
    id("com.android.library") version "8.7.3"
    id("com.android.application") version "8.7.3" apply false
}
android {
    namespace = "com.t34400.vrwebview.gameime.checks"
    compileSdk = 35
    ndkVersion = "27.2.12479018"
    defaultConfig { minSdk = 29; ndk { abiFilters += "arm64-v8a" } }
    buildFeatures { prefab = true }
    externalNativeBuild { cmake {
        path = file("CMakeLists.txt"); version = "3.22.1"
        buildStagingDirectory = file("${gradle.gradleUserHomeDir}/vrwebview-gameime-cxx-${projectDir.absolutePath.hashCode()}")
    } }
    testOptions { unitTests.isIncludeAndroidResources = true }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_11; targetCompatibility = JavaVersion.VERSION_11 }
}
dependencies {
    implementation(platform("org.jetbrains.kotlin:kotlin-bom:1.8.22"))
    implementation(mapOf("name" to "games-activity-4.4.0-vrwebview-ime", "ext" to "aar"))
    implementation("androidx.core:core:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.robolectric:robolectric:4.14.1")
}
tasks.withType<Test>().configureEach {
    systemProperty("robolectric.dependency.repo.url", "https://repo.maven.apache.org/maven2")
}

apply(from = "../../../Packages/com.t34400.vrwebview/Editor/GameActivityImeCompat/apply.gradle")

// Negative integration fixtures; not included in any application by default.
if (providers.gradleProperty("gameImeDuplicate").isPresent) {
    dependencies { implementation("androidx.games:games-activity:4.4.0") }
}
if (providers.gradleProperty("gameImeWrongOriginal").isPresent) {
    configurations.matching { it.name == "vrwebviewGameImeOriginal" }.configureEach {
        resolutionStrategy.force("androidx.games:games-activity:4.4.2")
    }
}
