---
name: chat-image-retrieval
description: Local immutable tool skill for loading previously stored images from the current Discord channel when the user refers to old visual context.
source: local
priority: 84
trigger_phrases:
  - 과거 이미지
  - 이전 이미지
  - 채팅 이미지
  - 예전에 올린 이미지
  - 아까 올린 이미지
  - 전에 올린 그림
  - 이미지 다시 봐
tool_names:
  - load_chat_image
  - load_chat_images
  - get_chat_history
  - search_chat_history
---
# Chat Image Retrieval Tool Skill

## 목적

사용자가 현재 메시지에 이미지를 직접 첨부하지 않았지만, 현재 Discord 채널에 과거에 올라온 이미지를 다시 보고 답변해야 할 때만 이 Skill을 사용합니다.

이 Skill은 로컬 source-of-truth Skill이며, DB에서 수정 가능한 Skill은 과거 이미지 로딩 tool을 열 수 없습니다.

## tool 사용 원칙

- 메시지 ID나 이미지 ID가 명확하면 `load_chat_image`를 사용합니다.
- 여러 이미지를 비교해야 하거나 사용자가 여러 장을 지칭하면 `load_chat_images`를 사용합니다.
- 대상 메시지를 찾기 위한 단서가 부족하면 `get_chat_history` 또는 `search_chat_history`로 현재 채널 기록에서 후보를 찾습니다.
- 현재 메시지에 직접 첨부된 이미지는 이미 AI 입력에 포함되어 있으므로 이 tool을 호출하지 않습니다.

## 채널 범위 규칙

- 현재 Discord 채널에 저장된 이미지만 다룹니다.
- 다른 채널에서 이미지를 가져올 수 있다고 가정하지 않습니다.
- 후보가 여러 개면 임의로 단정하지 말고 사용자에게 구분 단서를 요청합니다.

## 응답 규칙

- 이미지를 로드한 뒤에는 보이는 내용과 추정한 내용을 분리합니다.
- 이미지가 로드되지 않으면 어떤 단서가 더 필요한지 안내합니다.
