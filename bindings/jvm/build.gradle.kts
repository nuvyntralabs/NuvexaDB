plugins {
    kotlin("jvm") version "2.1.20"
    `java-library`
}

group = "nuventra"
version = "1.0.1"

repositories {
    mavenCentral()
}

dependencies {
    api("net.java.dev.jna:jna:5.17.0")
    api("org.json:json:20250107")
    testImplementation(kotlin("test"))
    testImplementation("org.junit.jupiter:junit-jupiter:5.12.2")
}

sourceSets {
    create("sampleJava") {
        java.srcDir("src/sampleJava/java")
        compileClasspath += sourceSets.main.get().output + configurations["compileClasspath"]
        runtimeClasspath += output + sourceSets.main.get().output + configurations["runtimeClasspath"]
    }
    create("sampleKotlin") {
        kotlin.srcDir("src/sampleKotlin/kotlin")
        compileClasspath += sourceSets.main.get().output + configurations["compileClasspath"]
        runtimeClasspath += output + sourceSets.main.get().output + configurations["runtimeClasspath"]
    }
}

tasks.test {
    useJUnitPlatform()
}

tasks.register<JavaExec>("runJavaSample") {
    group = "application"
    classpath = sourceSets["sampleJava"].runtimeClasspath
    mainClass.set("nuventra.nuvexadb.sample.JavaSample")
}

tasks.register<JavaExec>("runKotlinSample") {
    group = "application"
    classpath = sourceSets["sampleKotlin"].runtimeClasspath
    mainClass.set("nuventra.nuvexadb.sample.KotlinSampleKt")
}

kotlin {
    jvmToolchain(17)
}

java {
    withSourcesJar()
}
