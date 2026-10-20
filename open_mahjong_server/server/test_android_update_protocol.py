from .response import MessageInfo, Response


def test_existing_message_payload_does_not_gain_a_null_version_field():
    message = MessageInfo(title="原提示", content="原内容")
    assert message.model_dump(exclude_none=True) == {"title": "原提示", "content": "原内容"}


def test_version_mismatch_retains_legacy_fields_and_adds_required_protocol():
    response = Response(
        type="message", success=False, message="error_version",
        message_info=MessageInfo(
            title="版本检查失败", content="原有版本不符提示", required_release_version=31,
        ),
    ).model_dump(exclude_none=True)
    assert response["message"] == "error_version"
    assert response["message_info"] == {
        "title": "版本检查失败", "content": "原有版本不符提示", "required_release_version": 31,
    }
