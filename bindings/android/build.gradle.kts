plugins {
    id("com.android.library") version "8.7.3"
    kotlin("android") version "2.1.20"
}

group = "nuventra"
version = "1.0.4"

android {
    namespace = "nuventra.nuvexadb"
    compileSdk = 35
    defaultConfig {
        minSdk = 26
        consumerProguardFiles("consumer-rules.pro")
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    sourceSets {
        getByName("main") {
            kotlin.srcDir("../jvm/src/main/kotlin")
            jniLibs.srcDir("src/main/jniLibs")
        }
    }
}

kotlin {
    jvmToolchain(17)
}

repositories {
    google()
    mavenCentral()
}

dependencies {
    api("net.java.dev.jna:jna:5.17.0@aar")
    api("org.json:json:20250107")
}
