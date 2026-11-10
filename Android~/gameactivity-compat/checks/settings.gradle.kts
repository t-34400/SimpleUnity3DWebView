pluginManagement { repositories { google(); mavenCentral(); gradlePluginPortal() } }
dependencyResolutionManagement { repositories { google(); mavenCentral(); flatDir { dirs("../../../Packages/com.t34400.vrwebview/Editor/GameActivityImeCompat") } } }
rootProject.name = "GameActivityImeContractChecks"
include(":app")
