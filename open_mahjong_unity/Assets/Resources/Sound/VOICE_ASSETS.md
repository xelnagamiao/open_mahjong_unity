# 动作语音素材

`ting` 为“听”，`baoting` 为“报听”，`ding` 为“叮”。规则模块通过 `ActionVoice` 选择报声，按钮名称可以描述动作而不等同于口头报声。声明、取消和普通摸打需分别处理；牌谱及观战从自己的规则上下文选择语音。

2026-10-07 新增 `ttsmaker_204_xiaoxiao/hua.mp3`（“花”）、`piao.mp3`（“飘”）、`tianting.mp3`（“天听”），分别用于 MIL 上海敲麻补花、MIL 杭州财飘及 MIL 贵州原报/软报。三段使用 Microsoft `zh-CN-XiaoxiaoNeural`，通过 [edge-tts](https://github.com/rany2/edge-tts) 7.2.8 合成，默认语速、音高、音量，24 kHz 单声道 MP3。输入文本只有对应的声明词。

球球音色没有这三个词的专用素材，沿用 `SoundManager` 的默认音色回退加载潇潇素材，保持正确报声。其余文件为项目已有的 TTSMaker 素材，不因本次修正而替换。
