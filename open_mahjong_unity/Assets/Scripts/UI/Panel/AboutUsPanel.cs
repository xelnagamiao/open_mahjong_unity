using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class AboutUsPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text PanelText;
    [SerializeField] private Button GoToGithubButton;


    private void Awake()
    {
        GoToGithubButton.onClick.AddListener(GoToGithubButtonClicked);
        LoadAndDisplayContent();
    }

    private void LoadAndDisplayContent()
    {
    
        string content = "";

        // 按照指定顺序组合内容并调整格式美观
        // 1. 关于我们
        content += "=== 关于我们 ===\n\n";

        // 2. 如何添加自定义麻将规则
        content += "=== 如何添加自定义麻将规则 ===\n\n";
        content += "添加自定义麻将规则有三种方式，其一是提供核心的逻辑文件、项目组进行简单的适配；其二是发起pull request，将自己编写的新规则合并到项目的github仓库之上；其三是提供规则文件，委托项目开发人员开发相应的规则。\n\n";
        content += "需要注意的是，委托项目开发者开发的规则将会上传到公共的代码仓库，如果您想要使用pull request的方式，最好先在测试群进行沟通；代码提交到git仓库时，如果没有进行特殊声明，则默认您接受本项目的MIT协议。此外，如果有服务器遭到攻击或者其他无力支持服务器存续的情形，公共平台可能会关停，届时规则制定者可以自己部署服务器来支持自己自定义的规则，本项目不保证公共平台的持续服务。\n\n";
        content += "如果确定需要添加自定义的麻将规则，下面是详细的需求解释：\n\n";
        content += "在麻将游戏当中，一个规则的执行逻辑由三部分组成：\n";
        content += "1、麻将游戏服务器管理某个麻将规则的不同逻辑规则\n";
        content += "2、听牌检查脚本监听你的手牌是否符合合法的听牌形状\n";
        content += "3、和牌检查脚本结算和牌组合的具体牌型和分数\n\n";
        content += "首先、麻将游戏服务器会匹配玩家目前的状态和行为是否符合某个特定的操作规则，例如切牌、吃、碰、杠、加杠、自摸、荣和，均属于麻将游戏基础的操作规则，麻将游戏服务器会发送这些选项给玩家，玩家就可以从这些选项中选择自己要执行的部分。而补花、九种九牌、四风连打、换三张之类属于不同规则下的特殊操作；如果您所要添加的自定义规则中只包含基础操作规则、或者服务器中已被支持的其他规则当中所属的操作规则，那么就可复用或者修改现有的游戏服务器文件，可以不提供游戏服务器的相关文件。\n\n";
        content += "其次、听牌检查脚本会负责监听你的手牌是否符合合法的听牌规则，如果您所要添加的自定义规则中只包含一般形、七对、十三幺、全不靠等常见并且基础的牌型，那么就可复用现有的听牌检测文件，可以不提供听牌检测的相关文件。\n\n";
        content += "最后、和牌检查脚本负责结算和牌组合的具体牌型和分数，大多数麻将规则的主要变化也集中在和牌番役的变化上，一般添加规则只需要提供完整的和牌检查脚本即可。\n\n";
        content += "小贴士：服务器采用py-fastapi框架，您提供的麻将游戏服务器脚本需要是py文件，听牌检查脚本和和牌检查脚本可以是py、so或者其他语言编译的文件，如果您需要在客户端显示提示（在切牌以后显示和牌的番数），由于要在unity客户端中执行，需要提供C#文件或者C#编译的动态链接库dll文件。\n\n";

        // 3. 随机种子
        content += "=== 随机种子 ===\n\n";
        content += "为确保游戏的公平性，在每局游戏开始时，服务器都会生成一个全局随机种子（game_random_seed），这个种子贯穿整局游戏的所有小局。当每个小局开始时，系统会根据全局种子和当前局数相加计算出一个局内随机种子（round_random_seed），用于打乱该局的牌堆。\n\n";
        content += "在进行游戏的过程中，您可以随时在房间显示局数的左上角处使用左键单击，用于查看本局游戏的局内随机种子；在游戏结束时，完整的全局随机种子会出现在结算界面的右下角，也可以在游戏结束以后在牌谱功能中找到该局游戏中的局内、全局的随机种子。\n\n";
        content += "局内随机种子的计算公式如下：\n";
        content += "round_random_seed = (game_random_seed * 1000 + round_index) % 4294967296\n\n";
        content += "例如，如果全局种子是1234567890，第1局的局内种子就是1234567890001，第2局就是1234567890002，以此类推。\n\n";
        content += "需要注意的是，在某些游戏过程中，游戏局数和游戏局数的实际索引并不相通，例如日麻东一局的一本场，round_index则为2，而不是1。\n\n";
        content += "牌堆的打乱过程使用了Python的random.shuffle()函数。这个函数的工作原理是：首先使用random.seed()设置随机种子，然后对牌堆列表进行Fisher-Yates洗牌算法。具体来说，算法会从后往前遍历牌堆，每次随机选择一个位置（基于种子生成的随机数），然后将当前位置的牌与随机位置的牌交换。这样就能确保相同种子产生相同的牌序，让洗牌过程完全可复现。\n\n";
        content += "您可以使用python的random库在以下代码中对随机种子进行验证：\n\n";
        content += "# 游戏开始时生成全局随机种子\n";
        content += "game_random_seed = int(time.time() * 1000000) % (2**32)\n\n";
        content += "# 标准牌堆\n";
        content += "sth_tiles_set = {\n";
        content += "    11,12,13,14,15,16,17,18,19, # 万\n";
        content += "    21,22,23,24,25,26,27,28,29, # 饼\n";
        content += "    31,32,33,34,35,36,37,38,39, # 条\n";
        content += "    41,42,43,44, # 东南西北\n";
        content += "    45,46,47 # 中白发\n";
        content += "}\n";
        content += "# 花牌牌堆\n";
        content += "hua_tiles_set = {51,52,53,54,55,56,57,58} # 春夏秋冬 梅兰竹菊\n\n";
        content += "# 生成牌堆\n";
        content += "self.tiles_list = []\n";
        content += "for tile in sth_tiles_set:\n";
        content += "    self.tiles_list.extend([tile] * 4)\n";
        content += "self.tiles_list.extend(hua_tiles_set)\n\n";
        content += "# 计算局内随机种子\n";
        content += "self.round_random_seed = (self.game_random_seed * 1000 + self.current_round) % (2**32)\n";
        content += "random.seed(self.round_random_seed)\n";
        content += "random.shuffle(self.tiles_list)\n\n";

        // 4. 致谢
        content += "=== 致谢 ===\n\n";
        content += "牌面提供者：雪枫XueFun9\n";
        content += "新编MCR编著者：Natsuki\n";
        content += "参与测试人员：\n";

        // 设置到 PanelText 中
        if (PanelText != null)
        {
            PanelText.text = content;
        }
        else
        {
            Debug.LogError("PanelText 引用未设置！");
        }
    }

    private void GoToGithubButtonClicked()
    {
        Application.OpenURL(ConfigManager.githubUrl);
    }

}
