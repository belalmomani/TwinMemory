import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("com.google.devtools.ksp")
}

// Signing: read from keystore.properties (git-ignored) or environment. Never commit secrets.
val ksProps = Properties().apply {
    val f = rootProject.file("keystore.properties")
    if (f.exists()) f.inputStream().use { load(it) }
}
fun sign(key: String, env: String): String? = ksProps.getProperty(key) ?: System.getenv(env)

// Firebase plugin only when the (git-ignored) config file is present, so builds do not fail without it.
if (file("google-services.json").exists()) apply(plugin = "com.google.gms.google-services")

android {
    namespace = "com.belalmomani.twinmemory"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.belalmomani.twinmemory" // must never change
        minSdk = 24
        targetSdk = 34
        versionCode = 2
        versionName = "1.1.0"
        resourceConfigurations += listOf("ar", "en")
    }

    // One stable signing identity for every build, so each new APK installs as an UPDATE over the previous one (data kept).
    // Secrets (TM_KEYSTORE_* or keystore.properties) override it; otherwise the committed sideload key is used.
    signingConfigs {
        create("tm") {
            val path = sign("storeFile", "TM_KEYSTORE_PATH")
            if (path != null) {
                storeFile = file(path)
                storePassword = sign("storePassword", "TM_KEYSTORE_PASSWORD")
                keyAlias = sign("keyAlias", "TM_KEY_ALIAS")
                keyPassword = sign("keyPassword", "TM_KEY_PASSWORD")
            } else {
                storeFile = file("sideload.keystore")
                storePassword = "twinmemory"
                keyAlias = "twinmemory"
                keyPassword = "twinmemory"
            }
        }
    }

    buildTypes {
        debug { signingConfig = signingConfigs.getByName("tm") }
        release {
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = signingConfigs.getByName("tm")
        }
    }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true; buildConfig = true }
    composeOptions { kotlinCompilerExtensionVersion = "1.5.14" }
}

ksp { arg("room.schemaLocation", "$projectDir/schemas") }

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.09.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.activity:activity-compose:1.9.2")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.core:core-splashscreen:1.0.1")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.6")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.6")
    implementation("androidx.room:room-runtime:2.6.1")
    implementation("androidx.work:work-runtime-ktx:2.9.1")
    implementation("androidx.documentfile:documentfile:1.0.1")
    implementation("androidx.room:room-ktx:2.6.1")
    ksp("androidx.room:room-compiler:2.6.1")
    debugImplementation("androidx.compose.ui:ui-tooling")
    testImplementation("junit:junit:4.13.2")
}
