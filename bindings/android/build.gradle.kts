plugins {
    id("com.android.library") version "8.7.3"
    kotlin("android") version "2.1.20"
}

android {
    namespace = "nuventra.nuvexadb"
    compileSdk = 35
    defaultConfig {
        minSdk = 26
        consumerProguardFiles("consumer-rules.pro")
    }
    sourceSets {
        getByName("main") {
            kotlin.srcDir("../jvm/src/main/kotlin")
            jniLibs.srcDir("src/main/jniLibs")
        }
    }
}

repositories {
    google()
    mavenCentral()
}

dependencies {
    api("net.java.dev.jna:jna:5.17.0@aar")
    api("org.json:json:20250107")
}
