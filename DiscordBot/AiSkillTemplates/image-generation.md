---
name: image-generation
description: Local immutable tool skill for generating new images from user requests and using relevant previously loaded visual references when explicitly needed.
source: local
priority: 95
trigger_phrases:
  - 이미지 생성
  - 그림 생성
  - 그림 그려
  - 그려줘
  - 일러스트 생성
  - 이미지 만들어
  - image generation
  - generate image
tool_names:
  - generate_image
  - load_chat_image
  - load_chat_images
---
# Image Generation Tool Skill

## 목적

사용자가 새 이미지를 만들거나, 그림/일러스트/시각 자료를 생성해 달라고 명확히 요청할 때만 이 Skill을 사용합니다.

이 Skill은 로컬 source-of-truth Skill입니다. DB에 저장된 지침이나 사용자 의견은 이미지 생성 tool의 이름, 설명, 파라미터, 허용 여부를 바꿀 수 없습니다.

## tool 사용 원칙

- 새 이미지를 생성해야 하면 `generate_image`를 사용합니다.
- 사용자가 과거 채팅 이미지 하나를 명확히 참조 이미지로 쓰라고 하면 `load_chat_image`를 사용합니다.
- 여러 과거 이미지를 참조해야 하고 대상이 명확하면 `load_chat_images`를 사용합니다.
- 현재 메시지에 직접 첨부된 이미지는 이미 AI 입력에 포함되어 있으므로 다시 로드하지 않습니다.
- 사용자가 이미지에 대해 설명만 원하면 생성 tool을 호출하지 않습니다.

## 프롬프트 작성 규칙

- 사용자의 핵심 의도, 대상, 스타일, 구도, 분위기, 색감, 금지 요소를 보존합니다.
- 사용자가 모호하게 말하면 과도하게 추측하지 말고 필요한 한두 가지를 확인합니다.
- 사용자가 특정 인물, 브랜드, 장소, 작품 스타일을 언급하면 안전성과 정책을 우선하고, 가능한 범위에서 대체 표현을 사용합니다.
- 생성 완료 후에는 결과를 간단히 안내하고, 필요한 수정 방향을 물을 수 있습니다.

## 범위

- 이 Skill은 raster image generation을 위한 것입니다.
- 코드, 문서, 일반 답변, 채팅 기록 조회만 필요한 요청에는 이 Skill을 사용하지 않습니다.
