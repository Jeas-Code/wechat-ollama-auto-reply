# OCR models

WeChatAuto.SDK needs four OCR files at runtime. Download them from the upstream
[`Tools/models`](https://github.com/scottfly189/WeChatAuto.SDK/tree/master/Tools/models)
directory and place them here:

- `ch_PP-OCRv5_mobile_det.onnx`
- `ch_ppocr_mobile_v2.0_cls_infer.onnx`
- `ch_PP-OCRv5_rec_mobile_infer.onnx`
- `ppocrv5_dict.txt`

The binary model files are intentionally not committed to this repository.
You can instead keep them elsewhere and set `AICHAT_OCR_MODELS_DIR` to that
directory.
