require "json"

package = JSON.parse(File.read(File.join(__dir__, "package.json")))

Pod::Spec.new do |s|
  s.name         = "nuvexadb"
  s.version      = package["version"]
  s.summary      = package["description"]
  s.license      = package["license"]
  s.authors      = "Niladri Prasad Padhy"
  s.homepage     = "https://github.com/nuvyntralabs/NuvexaDB"
  s.platforms    = { :ios => "13.0" }
  s.source       = { :git => "https://github.com/nuvyntralabs/NuvexaDB.git", :tag => "v#{s.version}" }
  s.source_files = "ios/**/*.{h,m,mm}"
  s.libraries    = "nuvexa"
  s.pod_target_xcconfig = {
    "HEADER_SEARCH_PATHS" => "\"$(PODS_TARGET_SRCROOT)/ios/include\""
  }
  s.dependency "React-Core"
end
