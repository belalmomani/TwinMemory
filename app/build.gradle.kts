import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("com.google.gms.google-services")
}

// Signing: read from keystore.properties (git-ignored) or environment. Never commit secrets.
val ksProps = Properties().apply {
    val f = rootProject.file("keystore.properties")
    if (f.exists()) f.inputStream().use { load(it) }
}
fun sign(key: String, env: String): String? = ksProps.getProperty(key) ?: System.getenv(env)

android {
    namespace = "com.belalmomani.twinmemory"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.belalmomani.twinmemory" // must never change
        minSdk = 24
        targetSdk = 34
        versionCode = 1
        versionName = "1.0.0"
        resourceConfigurations += listOf("ar", "en")
    }

    signingConfigs {
        val path = sign("storeFile", "TM_KEYSTORE_PATH")
        if (path != null) {
            create("release") {
                storeFile = file(path)
                storePassword = sign("storePassword", "TM_KEYSTORE_PASSWORD")
                keyAlias = sign("keyAlias", "TM_KEY_ALIAS")
                keyPassword = sign("keyPassword", "TM_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfigs.findByName("release")?.let { signingConfig = it }
        }
    }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true }
    composeOptions { kotlinCompilerExtensionVersion = "1.5.14" }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.09.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.activity:activity-compose:1.9.2")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.core:core-splashscreen:1.0.1")
    debugImplementation("androidx.compose.ui:ui-tooling")
}
