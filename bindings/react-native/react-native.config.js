module.exports = {
  dependency: {
    platforms: {
      android: {
        sourceDir: "./android",
        packageImportPath: "import nuventra.nuvexadb.rn.NuvexaPackage;",
        packageInstance: "new NuvexaPackage()",
      },
      ios: {},
    },
  },
};
