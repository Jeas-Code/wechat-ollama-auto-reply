# Third-party notices

This project depends on the following separately maintained components:

- [FlaUI](https://github.com/FlaUI/FlaUI), MIT License. It provides Windows
  window capture and input primitives.
- [RapidOCRLib](https://github.com/scottfly189/RapidOCRLib), Apache-2.0 License.
  It provides local OCR inference.
- [Ollama](https://github.com/ollama/ollama), MIT License. It is called through
  its local HTTP API and is not redistributed by this repository.
- The optional PaddleOCR-compatible ONNX model files are downloaded separately
  from the WeChatAuto.SDK model directory and are not redistributed here.

Transitive dependency licenses remain the property of their respective
copyright holders. See the generated NuGet assets after `dotnet restore` for
the exact dependency graph used by a particular build.
