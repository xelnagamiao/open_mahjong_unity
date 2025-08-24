import time
start=time.time()
# Pai类是Paizu类中的手牌元素
class Pai(int):

    def __init__(self, value):
        super().__init__()
        self.partner_group = 0 # 拥有±1两张伙伴牌,可以以自身为中心组成顺子的数量的标记
        self.samenr = 0 # 相同牌标记
        self.highernr = 0 # 比自己更高的牌的标记
        self.smallernr = 0 # 比自己更小的牌的标记
        self.intnr = int(value) # Pai类的整数值
        self.sign = False # 用以在duizicheck、dazicheck与kezicheck中标识自身是否被使用过
        self.red = False # 标记赤宝牌

    def reset(self): # reset方法用以在paizu.check中重置pai类的属性
        self.partner_group = 0
        self.samenr = 0
        self.highernr = 0
        self.smallernr = 0
        self.sign = False
# Paizu类是主要的处理单元
class Paizu(list):
    def __init__(self, pais=None):
        super().__init__()
        if pais is None: # 传值为空则建立空列表
            pais = []
        self.extend(pais)  # 有传值则通过传值赋值自身列表
        
        self.roundnr = 0 # 代表向听数
        self.duizi = 0 # 代表对子数
        self.dazi = 0 # 代表搭子数
        self.kezi = 0 # 代表刻子数
        self.endhand = 0 # 存储最后一张手牌（自摸/荣和张）
        self.red_dora = 0 # 存储赤宝牌
        self.dazicheck = False # 代表是否还可以产生搭子
        self.kezicheck = False # 代表是否还可以产生刻子
        self.duizicheck = False # 代表是否还可以产生对子
        self.combinations = [] # 存储牌组中已经成型的组合 其中d代表对 k代表刻 s代表顺 例如 d12则代表在2索位置有一个对子
        self.combinations_MP = [] # 存储牌组中副露的组合
        self.duizicheck_ = False # 出现了产生多个对子的七对检测进入主牌组遍历，造成出现4个对子2个顺子的结果的bug,
        # 该数值为True则代表该牌组包含2个以上的对子，不再参与主牌组遍历的过程

        self.multiple_count = 0 # 1.在计分环节存储番数
        self.combinations_count = [] # 1.在计分环节存储牌型番
        self.m = 20 # 2.在计符环节存储符数
        self.m_combinations = [] # 2.在计符环节存储符数组合
        self.point_count = 0 # 3.存储计分
        self.point_result = "" # 4.存储计分描述
    def inherit(self,paizulist): # inherit方法用以在牌组进行多次check对牌组进行归纳的过程中继承先前牌组的属性
        self.roundnr = paizulist.roundnr # 继承向听数
        self.duizi = paizulist.duizi # 继承对子数
        self.dazi = paizulist.dazi # 继承搭子数
        self.kezi = paizulist.kezi # 继承刻子数
        self.combinations.extend(paizulist.combinations) # 继承牌组
        self.combinations_MP.extend(paizulist.combinations_MP) # 继承副露牌组
        self.duizicheck_ = paizulist.duizicheck_
        self.endhand = paizulist.endhand
        self.red_dora = paizulist.red_dora

    def check(self): # paizu中的check方法用以重置pai的属性
        nrlist = [item.intnr for item in self] # 生成一个数字列表供后续比对
        for i in self:
            i.reset() # 重置属性
            # 如果有更高,更低,相同的牌,则统计
            if i.intnr + 1 in nrlist:
                i.highernr = nrlist.count(i + 1)
            if i.intnr - 1 in nrlist:
                i.smallernr = nrlist.count(i - 1)
            if i.intnr in nrlist:
                i.samenr = nrlist.count(i) - 1 # 统计相同的牌时-1去掉自身
            # 高低牌选择更少的一边计算伙伴组合
            if i.highernr >= i.smallernr:
                i.partner_group = i.smallernr
            else:
                i.partner_group = i.highernr
            # 判断牌组是否已经达成了构成duizi,dazi和kezi的条件,并在牌组中记录
            if i.samenr >= 1:
                self.duizicheck = True
            if i.samenr >= 2:
                self.kezicheck = True
            if i.partner_group >= 1:
                self.dazicheck = True
# Mjobject 是前端request传值的对象
class Mjobject():
    def __init__(self):
        hand = ""
        inputMPdata1 = ""
        inputMPdata2 = ""
        inputMPdata3 = ""
        inputMPdata4 = ""
        way_to_hepai = []
        dora_num = ""
        deep_dora_num = ""
        position_select = ""
        public_position_select = ""
# majongdata 用于将手牌及副露传值处理为格式化数据
def majongdata(data):
    # 将红宝牌单独保存
    redsave = ""
    redsaves = ""
    redsavem = ""
    redsavep = ""
    for i in data:
        match i:
            case "0":
                redsave += i
            case "s":
                redsaves += redsave
                redsave = ""
            case "m":
                redsavem += redsave
                redsave = ""
            case "p":
                redsavep += redsave
                redsave = ""

    # 建立字牌和数牌匹配集
    savewordclass = {"东", "南", "西", "北", "发", "白", "中"}
    savenumclass = {"1","2","3","4","5","6","7","8","9"}
    # 建立暂存不同牌组的字符串
    saves = ""
    savem = ""
    savep = ""
    # 输出值
    savenumber = ""
    saveword = ""
    endsave = ""
    endsaveclass = ""
    for i in data:
        # 数字进入savenumber暂存字符串 字牌进入saveword暂存字符串
        # endsave 与 endsaveclass 用于在后续获取牌组的最后一张牌（自摸张）
        if i in savenumclass:
            savenumber += i
            endsave = i
        elif i in savewordclass:
            saveword += i
            endsave = i
        # 当遇到牌组标签s m p 时,将暂存的数据放入对应的标签集中
        elif i == "s":
            saves += savenumber
            savenumber = ""
            endsaveclass = "s"
        elif i == "m":
            savem += savenumber
            savenumber = ""
            endsaveclass = "m"
        elif i == "p":
            savep += savenumber
            savenumber = ""
            endsaveclass = "p"
        # 获得 saves,savem,savep,saveword 下一步进行合并
    mjsave=Paizu()
    # 将四个集合中的数据存储于牌组mjsave中
    for i in saves:
        mjsave.append(Pai(int(i)+10))
    for i in savem:
        mjsave.append(Pai(int(i)+20))
    for i in savep:
        mjsave.append(Pai(int(i)+30))
    for char in saveword:
        match char:
            case "东":
                mjsave.append(Pai(41))
            case "南":
                mjsave.append(Pai(44))
            case "西":
                mjsave.append(Pai(47))
            case "北":
                mjsave.append(Pai(50))
            case "白":
                mjsave.append(Pai(53))
            case "发":
                mjsave.append(Pai(56))
            case "中":
                mjsave.append(Pai(59))
    # 将endsave 合并至 mjsave.endhand
    if endsave in savenumclass:
        if endsaveclass == "s":
            endsave = int(endsave) + 10
        if endsaveclass == "m":
            endsave = int(endsave) + 20
        if endsaveclass == "p":
            endsave = int(endsave) + 30
    elif endsave in savewordclass:
        match endsave:
            case "东":
                mjsave.endhand = 41
            case "南":
                mjsave.endhand = 44
            case "西":
                mjsave.endhand = 47
            case "北":
                mjsave.endhand = 50
            case "白":
                mjsave.endhand = 53
            case "发":
                mjsave.endhand = 56
            case "中":
                mjsave.endhand = 59
    # 将红宝牌合并进主牌组
    redlist = []
    for i in range(len(redsaves)):
        redlist.append(Pai(15))
    for i in range(len(redsavem)):
        redlist.append(Pai(25))
    for i in range(len(redsavep)):
        redlist.append(Pai(35))
    for i in redlist:
        i.red = True
        mjsave.append(i)
        mjsave.red_dora += 1
    # 排序
    mjsave.sort(key=lambda x: x.intnr)
    return mjsave # 返回牌组mjsavelist
# 处理对子
def duizicheck(duizilist):
    outputlist = []
    while True: # 执行循环直到 break
        signnr = 2 # 控制标记牌数的变量 对子为2
        mjlist = Paizu()
        mjlist.inherit(duizilist) # 继承牌组属性
        for i in duizilist:
            # 如果有一张相同的牌 并且signnr大于1 没有被其他牌组使用过
            if i.samenr >= 1 and signnr >= 1 and i.sign == False:
                i.sign = True
                signnr -= 1
                if signnr == 0: # 如果标记牌数归零
                    mjlist.duizi += 1 # 对子数+1
                    mjlist.roundnr += 2  # 向听数+2
                    mjlist.combinations.append(f"d{i}") # 添加牌组标记
                    # 判断组2 用以改变周边牌类的标记
                    if i.samenr >=2: # 如果有三张一样的牌,可能会有产生2个对子的可能,以下循环完成全部标记
                        for item in duizilist:
                            if item.intnr == i.intnr:
                                item.sign = True
            else: # 如果没有被标记则存储
                mjlist.append(i)
        # 存储mjlist 如果标记牌数没有归零 说明没有更多的对子组合 结束while循环
        outputlist.append(mjlist)
        if signnr == 2:
            return outputlist
# 处理刻子
def kezicheck(kezilist):
    outputlist = []
    while True:
        mjlist=Paizu()
        mjlist.inherit(kezilist) # 同步牌组属性
        signnr = 3  # 控制标记牌数的变量 刻子为3
        for i in kezilist:
            # 判断一张牌是否构成刻子可能,并且将该牌进行标记
            if i.samenr >= 2 and signnr >= 1 and i.sign == False: # 如果breaknr尚未被清空 且done未被标记
                i.sign = True
                signnr -= 1
                if signnr == 0:
                    mjlist.kezi += 1  # 刻子数+1
                    mjlist.roundnr += 3  # 向听数+3
                    mjlist.combinations.append(f"k{i}")  # 添加牌组标记
                    # 判断组2 用以改变周边牌类的标记
                    for item in kezilist:
                        if item.intnr == i.intnr:
                            item.sign = True
            # 操作组 将未被标记的牌组进行添加
            else:
                mjlist.append(i)
        if signnr == 3:
            return outputlist
        outputlist.append(mjlist)
# 处理搭子(顺子)
def dazicheck(dazilist):
    outputlist = []
    while True:
        mjlist=Paizu()
        mjlist.inherit(dazilist)
        signnr = 3
        samenr = 0
        for i in dazilist:
            # 判断一张牌是否构成搭子可能,并且将该牌进行标记
            if i.partner_group >= 1 and signnr == 3 and i.sign == False:
                mjlist.dazi += 1
                mjlist.roundnr += 3
                mjlist.combinations.append(f"s{i}")
                samenr = i.intnr
                for item in dazilist:
                    if item.intnr == samenr:
                        item.sign = True
                break
        for item in dazilist:
            # 判断牌组是否被标记，或是否是被标记牌的相邻牌，如不是则存储
            if item.intnr == samenr - 1 and signnr == 3:
                signnr -= 1
            elif item.intnr == samenr and signnr == 2:
                signnr -= 1
            elif item.intnr == samenr + 1 and signnr == 1:
                signnr -= 1
            else:
                mjlist.append(item)
        if signnr == 3:
            return outputlist
        outputlist.append(mjlist)
# MPcheck 将副露/鸣牌数据处理完成后合并至主牌组的属性中
def MPcheck(mplist,inputdata):
    # 如果mplist的元素等于4 添加明杠 等于5 添加暗杠
    if len(mplist) == 4:
        for item in mplist:
            inputdata.combinations_MP.append(f"g{item}")
            inputdata.roundnr += 3
            mplist == 0
            break
    else:
        if len(mplist) == 5:
            for item in mplist:
                inputdata.combinations.append(f"g{item}")
                inputdata.roundnr += 3
                mplist == 0
                break
        # 非杠情况执行一般型检测
        else:
            MPlist = []
            savelist = []
            MPlistoutput = []
            MPlist.append(mplist)
            while MPlist:
                for i in MPlist:
                    i.check() # 自检
                    if i.kezicheck == True and i.dazicheck == False:  # 如果一个牌可以构成刻子
                        savelist.extend(kezicheck(i)) # 进行刻子运算
                    elif i.dazicheck == True and i.kezicheck == False:
                        savelist.extend(dazicheck(i)) # 进行搭子运算
                    elif i.dazicheck == False and i.kezicheck == False:
                        MPlistoutput.append(i)
                MPlist = savelist
                savelist = []
            for i in MPlistoutput:
                inputdata.roundnr += i.roundnr # 继承向听数
                inputdata.duizi += i.duizi # 继承对子数
                inputdata.dazi += i.dazi # 继承搭子数
                inputdata.kezi += i.kezi # 继承刻子数
                inputdata.combinations_MP.extend(i.combinations) # 继承牌组
                for item in i:
                    print("检测到副露输入中包含未成搭，刻")
                    break
# GScheck与QDcheck专门用于处理非一般型 国士无双/七对子
def GScheck(yaojiulist,alllist):
    yaojiu = {11, 19, 21, 29, 31, 39, 41, 44, 47, 50, 53, 56, 59}
    mjlist = Paizu()
    samenr = 0
    keynr = 0
    for i in yaojiulist:
        if i.intnr in yaojiu and samenr != i.intnr or keynr == 0:
            if i.samenr != i.intnr:
                keynr += 1
            samenr = i.intnr
            mjlist.roundnr += 1
        else:
            mjlist.append(i)
    if mjlist.roundnr == 14:
        mjlist.combinations.append("y13")
    else:
        mjlist.combinations.append("?y13")
    alllist.append(mjlist)
    print("十三幺遍历：向听数为",14-mjlist.roundnr,"包含的牌组包括",mjlist.combinations,"剩余的牌包括",mjlist)
def QDcheck(duizilist,alllist):
    savelist = duizilist
    savelist.check()
    while True :
        signnr = 2
        mjlist = Paizu()
        mjlist.inherit(savelist) # 继承牌组属性
        for i in savelist:
            # 如果有一张相同的牌 并且signnr大于1 没有被其他牌组使用过
            if i.samenr >= 1 and signnr >= 1 and i.sign == False:
                i.sign = True
                signnr -= 1
                if signnr == 0:  # 如果标记牌数归零
                    mjlist.duizi += 1  # 对子数+1
                    mjlist.roundnr += 2  # 向听数+2
                    if (f"d{i}") in mjlist.combinations:
                        mjlist.append(i)
                        mjlist.append(i)
                        mjlist.duizi -= 1
                        mjlist.roundnr -= 2
                        signnr = 2
                    else:
                        mjlist.combinations.append(f"d{i}")  # 添加牌组标记
                    # 判断组2 用以改变周边牌类的标记
                    if i.samenr >= 3:  # 如果有三张一样的牌,可能会有产生2个对子的可能,以下循环完成全部标记
                        for item in duizilist:
                            if item.intnr == i.intnr:
                                item.sign = True
            else:  # 如果没有被标记则存储
                mjlist.append(i)
            # 存储mjlist 如果标记牌数没有归零 说明没有更多的对子组合 结束while循环
        savelist = mjlist
        savelist.check()
        if signnr == 2:
            if savelist.roundnr > 2:
                savelist.duizicheck_ = True
            alllist.append(savelist)
            print("七对子遍历：向听数为",14-mjlist.roundnr,"包含的牌组包括",mjlist.combinations,"剩余的牌包括",mjlist)
            break
# 主牌组回溯法迭代
def handCheck(alllist):
    endlist = []
    r = 0
    while alllist:
        savelist = []
        residue_paizu = 0 # 监控savelist中一共有多少牌组
        endlist_paizu = 0
        for i in alllist:
            i.check() # 自检
            if i.duizicheck_ is not True:
                if i.kezicheck == True and i.dazicheck == True:  # 如果一个牌组又可以构成搭子又可以构成刻子
                    savelist.extend(dazicheck(i)) # 进行搭子运算
                    savelist.extend(kezicheck(i)) # 进行刻子运算 !如先进行刻子运算会导致 i(Pai).sign 未还原
                    residue_paizu += 2
                elif i.kezicheck == True and i.dazicheck == False:  # 如果一个牌可以构成刻子
                    savelist.extend(kezicheck(i)) # 进行刻子运算
                    residue_paizu += 1
                elif i.dazicheck == True and i.kezicheck == False:
                    savelist.extend(dazicheck(i)) # 进行搭子运算
                    residue_paizu += 1
                elif i.dazicheck == False and i.kezicheck == False:
                    endlist.append(i)
                    endlist_paizu += 1
            else:
                endlist.append(i)
        alllist = savelist
        savelist = []
        r += 1
        print(f"第{r}轮遍历剩余牌组还有", residue_paizu)
        print("完成牌组总共", endlist_paizu)
    return endlist
# resultFilter 用于保留离结果最近的分支
def resultFilter(endlist):
    maxroundnr = 0
    mjlist = []
    for i in endlist:
        if i.roundnr >= maxroundnr :
             maxroundnr = i.roundnr
    for i in endlist:
        if i.roundnr == maxroundnr :
            mjlist.append(i)
            i.roundnr = 14-i.roundnr
    for i in mjlist:
        print("最低向听数为",i.roundnr,"包含的牌组包括",i.combinations,"副露牌组包括",i.combinations_MP,"剩余的牌包括",i)
    return mjlist
# distinctMjlist 用于合并相同分支下的结果
def distinctMjlist(mahjonglist):
    mjlist = []
    all_combinations = []
    all_combinations_list = []
    for i in mahjonglist:
        all_combinations = sorted(i.combinations + i.combinations_MP)
        if all_combinations not in all_combinations_list :
            mjlist.append(i)
            all_combinations_list.append(all_combinations)
            print(all_combinations)
    return mjlist
# multple_count 计番 使用部分中文变量
def multple_count(mahjonglist,Mj_input,inputdata):
    # part0 定义部分番种的查表集合
    chunquanset = {"s12", "s18", "s22", "s28", "s32", "s38",
                   "d11", "d19", "d21", "d29", "d31", "d39",
                   "k11", "k19", "k21", "k29", "k31", "k39"}  # 幺九的牌组集合
    hunquanset = {"s12", "s18", "s22", "s28", "s32", "s38",
                  "d11", "d19", "d21", "d29", "d31", "d39",
                  "k11", "k19", "k21", "k29", "k31", "k39",
                  "d41", "d44", "d47", "d50", "d53", "d56", "d59",
                  "k41", "k44", "k47", "k50", "k53", "k56", "k59"}  # 字牌、幺九的牌组集合
    zipaiset = {41, 44, 47, 50, 53, 56, 59}  # 字牌集合
    suoziset = {11, 12, 13, 14, 15, 16, 17, 18, 19}  # 索子集合
    wanziset = {21, 22, 23, 24, 25, 26, 27, 28, 29}  # 万子集合
    tongziset = {31, 32, 33, 34, 35, 36, 37, 38, 39}  # 筒子集合
    hunlaotouset = {11, 19, 21, 29, 31, 39, 41, 44, 47, 50, 53, 56, 59}  # 幺九集合
    duanyaoset = {12, 13, 14, 15, 16, 17, 18, 21, 22, 23, 24, 25, 26, 27, 28, 32, 33, 34, 35, 36, 37, 38}  # 断幺集合
    sanyuanset = {"d53", "d56", "d59", "k53", "k56", "k59"}  # 小三元集合
    sanyuankeset = {"k53", "k56", "k59"}  # 大三元集合
    positionset = {"d53,d56,d59"} # 自风场风集合 用以判断是否构成平和

    # part1 通过 传值检测 判断全局变量
    自摸 = False
    立直 = False
    双立直 = False
    荣和 = False
    一发 = False
    河底 = False
    海底 = False
    岭上 = False
    抢杠 = False
    for i in Mj_input.way_to_hepai:
        match i:
            case "wayToHepaiZi":
                自摸 = True
            case "wayToHepaiLi":
                立直 = True
            case "wayToHepaiDoubleLi":
                双立直 = True
            case "wayToHepaiRo":
                荣和 = True
            case "wayToHepaiYi":
                一发 = True
            case "wayToHepaiHe":
                河底 = True
            case "wayToHepaiHai":
                海底 = True
            case "wayToHepaiLin":
                岭上 = True
            case "wayToHepaiQG":
                抢杠 = True

    # part2 通过 手牌检测 判断全局变量
    清一色 = False
    混一色 = False
    混老头 = False
    断幺 = False
    all_tiles = majongdata(Mj_input.hand+Mj_input.inputMPdata1+Mj_input.inputMPdata2+Mj_input.inputMPdata3+Mj_input.inputMPdata4)
    print(all_tiles)
    if all(10 < element < 20 for element in all_tiles):
        清一色 = True
    elif all(20 < element < 30 for element in all_tiles):
        清一色 = True
    elif all(30 < element < 40 for element in all_tiles):
        清一色 = True
    elif all(10 < element < 20 or 40 < element for element in all_tiles):
        混一色 = True
    elif all(20 < element < 30 or 40 < element for element in all_tiles):
        混一色 = True
    elif all(30 < element < 40 or 40 < element for element in all_tiles):
        混一色 = True
    if all(element in hunlaotouset for element in all_tiles):
        混老头 = True
    if all(element in duanyaoset for element in all_tiles):
        断幺 = True

    副露 = False
    自风 = ""
    场风 = ""
    宝牌 = 0
    里宝牌 = 0
    if Mj_input.dora_num:
        宝牌 = int(Mj_input.dora_num)
    if Mj_input.deep_dora_num:
        里宝牌 = int(Mj_input.deep_dora_num)

    # part3 通过 传值检测 获取全局变量
    match Mj_input.position_select :
        case "positionDong":
            自风 = "东"
            positionset.add("d41")
        case "positionNan":
            自风 = "南"
            positionset.add("d44")
        case "positionXi":
            自风 = "西"
            positionset.add("d47")
        case "positionBei":
            自风 = "北"
            positionset.add("d50")
        case "positionOther":
            自风 = "闲家"
            pass
    match Mj_input.public_position_select :
        case "publicPositionDong":
            场风 = "东"
            positionset.add("d41")
        case "publicPositionNan":
            场风 = "南"
            positionset.add("d44")
        case "publicPositionXi":
            场风 = "西"
            positionset.add("d47")
        case "publicPositionBei":
            场风 = "北"
            positionset.add("d50")
    if Mj_input.inputMPdata1 + Mj_input.inputMPdata2 + Mj_input.inputMPdata3 + Mj_input.inputMPdata4:
        副露 = True

    # 检测牌组
    for i in mahjonglist:
        all_combinations = sorted(i.combinations + i.combinations_MP)

        # 1.通过遍历全部组合的类型判断 断幺 混全 纯全
        if 断幺 == False :
            if all(element in hunquanset for element in all_combinations):
                if all(element in chunquanset for element in all_combinations):
                    if 副露 == False:
                        i.combinations_count.append("纯全")
                        i.multiple_count += 3
                    else:
                        i.combinations_count.append("副露纯全")
                        i.multiple_count += 2
                else:
                    if 副露 == False:
                        i.combinations_count.append("混全")
                        i.multiple_count += 2
                    else:
                        i.combinations_count.append("副露混全")
                        i.multiple_count += 1

        # 2.通过遍历所有组合判断 场风 自风 以及 三元刻 小三元
        str_combinations = ""
        sanyuannr = 0
        for item in all_combinations:
            match item:
                case "k53":
                    i.combinations_count.append("役牌白")
                    i.multiple_count += 1
                case "k56":
                    i.combinations_count.append("役牌发")
                    i.multiple_count += 1
                case "k59":
                    i.combinations_count.append("役牌中")
                    i.multiple_count += 1
                case "k41":
                    if 自风 == "东" :
                        i.combinations_count.append("自风东")
                        i.multiple_count += 1
                    if 场风 == "东" :
                        i.combinations_count.append("场风东")
                        i.multiple_count += 1
                case "k44":
                    if 自风 == "南":
                        i.combinations_count.append("自风南")
                        i.multiple_count += 1
                    if 场风 == "南":
                        i.combinations_count.append("场风南")
                        i.multiple_count += 1
                case "k47":
                    if 自风 == "西":
                        i.combinations_count.append("自风西")
                        i.multiple_count += 1
                    if 场风 == "西":
                        i.combinations_count.append("场风西")
                        i.multiple_count += 1
                case "k50":
                    if 自风 == "北":
                        i.combinations_count.append("自风北")
                        i.multiple_count += 1
                    if 场风 == "北":
                        i.combinations_count.append("场风北")
                        i.multiple_count += 1
            str_combinations += item # 合并all_combinations 中的所有字符串
            if item in sanyuanset:
                sanyuannr += 1
        if sanyuannr == 3:
            i.combinations_count.append("小三元")
            i.multiple_count += 2

        # 3.通过判断all_combinations中的刻对顺标记判断 平和 对对和 七对子 三杠子 四杠子
        kezinr = 0
        dazinr = 0
        duizinr = 0
        gangnr = 0
        for item in str_combinations:
            match item:
                case "d":
                    duizinr += 1
                case "k":
                    kezinr += 1
                case "s":
                    dazinr += 1
                case "g":
                    gangnr += 1
        if dazinr == 4: # 因为坎张和牌不算做平和 平和处理中需要生成荣和自摸牌能够构成两面的两种可能，并且这种可能组合在all_combinnations中被发现
            possible_combinnations = []
            possible_combinnations.append( "s" + str(i.endhand + 1))
            possible_combinnations.append( "s" + str(i.endhand - 1))
            if any(element in all_combinations for element in possible_combinnations):
                if 副露 == False:
                    if any(element in all_combinations for element in positionset):
                        pass # 如果对子牌是自身的场风或自风,平和不成立
                    else:
                        i.combinations_count.append("平和")
                        i.multiple_count += 1
        elif kezinr == 4:
            i.combinations_count.append("对对和")
            i.multiple_count += 2
        elif duizinr == 7:
            i.combinations_count.append("七对子")
            i.multiple_count += 2
        elif gangnr == 3:
            i.combinations_count.append("三杠子")
            i.multiple_count += 2
        elif gangnr == 4:
            i.combinations_count.append("四杠子")

        # 4.通过判断str_main_combinations 中的刻对顺标记判断 三暗刻 四暗刻
        str_main_combinations = ""
        for item in i.combinations:
            str_main_combinations += item
        kezinr = 0
        for item in str_main_combinations:
            match item:
                case "k":
                    kezinr += 1
                case "g":
                    kezinr += 1 # 主牌组中的杠属于暗杠 计算暗刻
        if kezinr == 3 :
            i.combinations_count.append("三暗刻")
            i.multiple_count += 2
        if kezinr == 4 :
            i.combinations_count.append("四暗刻")

        # 5.通过遍历全部组合的类型判断 一气
        if all(element in all_combinations for element in ["s12","s15","s18"]):
            if 副露 == False:
                i.combinations_count.append("一气贯通")
                i.multiple_count += 2
            else:
                i.combinations_count.append("副露一气贯通")
                i.multiple_count += 1
        elif all(element in all_combinations for element in ["s22","s25","s28"]):
            if 副露 == False:
                i.combinations_count.append("一气贯通")
                i.multiple_count += 2
            else:
                i.combinations_count.append("副露一气贯通")
                i.multiple_count += 1
        elif all(element in all_combinations for element in ["s22", "s25", "s28"]):
            if 副露 == False:
                i.combinations_count.append("一气贯通")
                i.multiple_count += 2
            else:
                i.combinations_count.append("副露一气贯通")
                i.multiple_count += 1

        # 6.通过对遍历项计数判断 一杯口 二杯口
        if 副露 == False:
            sameitem = ""
            for item in all_combinations:
                samenr = all_combinations.count(item)
                if samenr >= 2 and item != sameitem:
                    sameitem = item
                    if "一杯口" in i.combinations_count:
                        i.combinations_count.append("二杯口")
                        i.multiple_count += 1
                        i.combinations_count.remove("一杯口")
                    else:
                        i.combinations_count.append("一杯口")
                        i.multiple_count += 1

        # 7.通过切片取尾计同的方式 如有三个尾数一致的 判断 三色同顺 三色同刻
        same_dazi_item = ""
        same_kezi_item = ""
        dazislice = []
        kezislice = []
        for item in all_combinations:
            if "s" in item and item != same_dazi_item:
                same_dazi_item = item
                dazislice.append(item[2:3])
            if "k" in item:
                same_dazi_item = item
                kezislice.append(item[2:3])
        for item in dazislice:
            if dazislice.count(item) == 3 :
                if 副露 == False:
                    i.combinations_count.append("三色同顺")
                    i.multiple_count += 2
                    break # 三色同顺三色同刻只能计一次
                else:
                    i.combinations_count.append("副露三色同顺")
                    i.multiple_count += 1
                    break
        for item in kezislice:
            if kezislice.count(item) == 3 :
                i.combinations_count.append("三色同刻")
                i.multiple_count += 2
                break

        # 8.统计前3part的 传值番数 立直 双立直 一发 河底 海底 岭上 抢杠 自摸 以及全局番数 清混一色 混老头 断幺
        if 立直 == True:
            i.combinations_count.append("立直")
            i.multiple_count += 1
        if 双立直 == True:
            i.combinations_count.append("双立直")
            i.multiple_count += 2
        if 一发 == True:
            i.combinations_count.append("一发")
            i.multiple_count += 1
        if 河底 == True:
            i.combinations_count.append("河底")
            i.multiple_count += 1
        if 海底 == True:
            i.combinations_count.append("海底")
            i.multiple_count += 1
        if 岭上 == True:
            i.combinations_count.append("岭上")
            i.multiple_count += 1
        if 抢杠 == True:
            i.combinations_count.append("抢杠")
            i.multiple_count += 1
        if 断幺 == True:
            i.combinations_count.append("断幺")
            i.multiple_count += 1
        if 混老头 == True:
            i.combinations_count.append("混老头")
            i.multiple_count += 2
        if 副露 == False:
            if 清一色 == True:
                i.combinations_count.append("清一色")
                i.multiple_count += 6
            if 混一色 == True:
                i.combinations_count.append("混一色")
                i.multiple_count += 3
            if 自摸 == True:
                i.combinations_count.append("自摸")
                i.multiple_count += 1
        else:
            if 清一色 == True:
                i.combinations_count.append("副露清一色")
                i.multiple_count += 5
            if 混一色 == True:
                i.combinations_count.append("副露混一色")
                i.multiple_count += 2
        i.multiple_count += 宝牌
        i.multiple_count += 里宝牌
        i.multiple_count += i.red_dora
# m_count 计符
def m_count(mahjonglist,Mj_input):
    keziset={"k12","k13","k14","k15","k16","k17","k18","k22","k23","k24","k25","k26","k27","k28","k32","k33","k34","k35","k36","k37","k38",} # 中张刻 门清4符 副露2符
    gangset={"g12","g13","g14","g15","g16","g17","g18","g22","g23","g24","g25","g26","g27","g28","g32","g33","g34","g35","g36","g37","g38",} # 杠牌 门清16符 副露8符
    yaojiukeziset={"k11","k19","k21","k29","k31","k39","k41", "k44", "k47", "k50", "k53", "k56", "k59"} # 幺九刻 门清8符 副露4符
    yaojiugangset={"g11","g19","g21","g29","g31","g39","g41", "g44", "g47", "g50", "g53", "g56", "g59"} # 幺九杠 门清32符 副露16符
    duiziyipaiset={"d53", "d56", "d59"} # 三元牌 2符
    self_positionset=set() # 自风牌 2符
    public_positionset=set() # 场风牌 2符
    match Mj_input.position_select : # 为后续代码简洁暂时使用单元素集合
        case "positionDong":
            self_positionset.add("d41")
        case "positionNan":
            self_positionset.add("d44")
        case "positionXi":
            self_positionset.add("d47")
        case "positionBei":
            self_positionset.add("d50")
    match Mj_input.public_position_select :
        case "publicPositionDong":
            public_positionset.add("d41")
        case "publicPositionNan":
            public_positionset.add("d44")
        case "publicPositionXi":
            public_positionset.add("d47")
        case "publicPositionBei":
            public_positionset.add("d50")
    for i in mahjonglist:
        i.m = 20 # 初始符20
        i.m_combinations.append("基础符20")
        if "wayToHepaiZi" in Mj_input.way_to_hepai:
            i.m += 2 # 自摸符2
            i.m_combinations.append("自摸+2符")
        if f"k{i.endhand}" in i.combinations + i.combinations_MP: # 如果自摸牌构成刻子则跳过
            pass
        else:
            i.m += 2 # 如果自摸牌不构成刻子(即并非对碰 坎张 边张 单骑 +2符 )
            i.m_combinations.append("坎张、边张、单骑+2符")
        if "七对子" in i.combinations_count:
            i.m = 25 # 七对子25符
            i.m_combinations = ["七对子25符"]
        elif "平和" in i.combinations_count and "自摸" in i.combinations_count:
            i.m = 20 # 平和20符
            i.m_combinations = ["平和20符"]
        else:
            str_combinnations = ""
            s = 0
            for item in i.combinations + i.combinations_MP:
                str_combinnations += item
                s = str_combinnations.count("s")
            positionset = self_positionset | public_positionset
            if s == 4 and i.combinations_MP and any(element in i.combinations + i.combinations_MP for element in positionset) == False: # 有副露的平和型
                i.m = 30
                i.m_combinations = ["有副露的平和型30符"]
            else:
                for item in i.combinations:
                    if item in duiziyipaiset:
                        i.m += 2
                        i.m_combinations.append("役牌对+2符")
                    if item in self_positionset:
                        i.m += 2
                        i.m_combinations.append("自风对+2符")
                    if item in public_positionset:
                        i.m += 2
                        i.m_combinations.append("场风对+2符")
                    if item in keziset:
                        i.m += 4
                        i.m_combinations.append("数牌暗刻+4符")
                    if item in yaojiukeziset:
                        i.m += 8
                        i.m_combinations.append("幺九/字牌暗刻+8符")
                    if item in gangset:
                        i.m += 16
                        i.m_combinations.append("数牌暗杠+16符")
                    if item in yaojiugangset:
                        i.m += 32
                        i.m_combinations.append("幺九/字牌暗杠+32符")
                for item in i.combinations_MP:
                    if item in keziset:
                        i.m += 2
                        i.m_combinations.append("数牌明刻+2符")
                    if item in yaojiukeziset:
                        i.m += 4
                        i.m_combinations.append("幺九/字牌明刻+4符")
                    if item in gangset:
                        i.m += 8
                        i.m_combinations.append("数牌明杠+8符")
                    if item in yaojiugangset:
                        i.m += 16
                        i.m_combinations.append("幺九/字牌明杠+16符")
                if "wayToHepaiRo" in Mj_input.way_to_hepai and not any([Mj_input.inputMPdata1, Mj_input.inputMPdata2, Mj_input.inputMPdata3, Mj_input.inputMPdata4]):
                    i.m += 10 # 门清荣和 10符
                    i.m_combinations.append("门清荣和+10符")
# point_count 计算得点
def point_count(mahjonglist,Mj_input):
    # 切上计符
    for i in mahjonglist:
        if i.m % 10 != 0:
            if i.m != 25:
                m_point = (10 - i.m % 10) + i.m
            else:
                m_point = i.m
        else:
            m_point = i.m
        multple_count = (i.multiple_count + i.red_dora + (int(Mj_input.dora_num) if Mj_input.dora_num.isdigit() else 0)
                         + (int(Mj_input.deep_dora_num) if Mj_input.deep_dora_num.isdigit() else 0))
        if i.m * 2 ** (multple_count + 2) * 4 > 8000:
            # 得分超过满贯
            if multple_count >= 4:
                base_point = 2000
                point_sign = "满贯"
                if multple_count >= 6:
                    base_point = 3000
                    point_sign = "跳满"
                    if multple_count >= 8:
                        base_point = 4000
                        point_sign = "倍满"
                        if multple_count >= 11:
                            base_point = 6000
                            point_sign = "三倍满"
                            if multple_count >= 13:
                                base_point = 8000
                                point_sign = "累积役满"
            if "wayToHepaiZi" in Mj_input.way_to_hepai:
                # 亲家自摸
                if Mj_input.position_select == "positionDong":
                    i.point_result = f"亲家{point_sign}——自摸{base_point * 2}all"
                    i.point_count = base_point * 6
                # 闲家自摸
                else:
                    hand_point_1 = base_point * 2
                    hand_point_2 = base_point * 1
                    i.point_result = f"闲家{point_sign}——自摸{hand_point_1}-{hand_point_2}"
                    i.point_count = base_point * 4
            else:
                # 闲家荣和
                if Mj_input.position_select == "positionDong":
                    i.point_result = f"亲家{point_sign}——荣和{base_point * 6}点"
                    i.point_count = base_point * 6
                # 闲家荣和
                else:
                    i.point_result = f"闲家{point_sign}——荣和{base_point * 4}点"
                    i.point_count = base_point * 4
        else:
            # 得分未超过满贯
            if "wayToHepaiZi" in Mj_input.way_to_hepai:
                # 亲家自摸
                if Mj_input.position_select == "positionDong":
                    i.point_count = m_point * 2 ** (multple_count + 2) * 2
                    if i.point_count % 100 != 0:
                        i.point_count += 100 - i.point_count % 100
                    i.point_result = f"亲家自摸{base_point}all"
                    i.point_count = m_point * 6
                # 闲家自摸
                else:
                    hand_point_1 = m_point * 2 ** (multple_count + 2) * 2
                    if hand_point_1 % 100 != 0:
                        hand_point_1 += 100 - hand_point_1 % 100
                    hand_point_2 = m_point * 2 ** (multple_count + 2) * 1
                    if hand_point_2 % 100 != 0:
                        hand_point_2 += 100 - hand_point_2 % 100
                    i.point_result = f"闲家自摸{hand_point_1}-{hand_point_2}"
                    i.point_count = m_point * 4
            else:
                # 亲家荣和
                if Mj_input.position_select == "positionDong":
                    i.point_count = m_point * 2 ** (multple_count + 2) * 6
                    if i.point_count % 100 != 0:
                        i.point_count += 100 - i.point_count % 100
                    i.point_result = f"亲家荣和{i.point_count}"
                # 闲家荣和
                else:
                    i.point_count = m_point * 2 ** (multple_count + 2) * 4
                    if i.point_count % 100 != 0:
                        i.point_count += 100 - i.point_count % 100
                    i.point_result = f"闲家荣和{i.point_count}"

# 合并计番、计符、计分结果,输出运算结果
def multple_count_output(mahjonglist,Mj_input,inputdata,inputMPdata1,inputMPdata2,inputMPdata3,inputMPdata4):
    output = ""
    for i in mahjonglist:
        # 1.输出手牌图片组
        outputlist = [inputdata,inputMPdata1,inputMPdata2,inputMPdata3,inputMPdata4]
        for hand_list in outputlist:
            for item in hand_list:
                match item:
                    case 11:
                        output += "<img src ='./static/image/image_mj/1s.gif'>"
                    case 12:
                        output += "<img src ='./static/image/image_mj/2s.gif'>"
                    case 13:
                        output += "<img src ='./static/image/image_mj/3s.gif'>"
                    case 14:
                        output += "<img src ='./static/image/image_mj/4s.gif'>"
                    case 15:
                        if item.red == True:
                            output += "<img src ='./static/image/image_mj/0s.gif'>"
                        else:
                            output += "<img src ='./static/image/image_mj/5s.gif'>"
                    case 16:
                        output += "<img src ='./static/image/image_mj/6s.gif'>"
                    case 17:
                        output += "<img src ='./static/image/image_mj/7s.gif'>"
                    case 18:
                        output += "<img src ='./static/image/image_mj/8s.gif'>"
                    case 19:
                        output += "<img src ='./static/image/image_mj/9s.gif'>"
                    case 21:
                        output += "<img src ='./static/image/image_mj/1m.gif'>"
                    case 22:
                        output += "<img src ='./static/image/image_mj/2m.gif'>"
                    case 23:
                        output += "<img src ='./static/image/image_mj/3m.gif'>"
                    case 24:
                        output += "<img src ='./static/image/image_mj/4m.gif'>"
                    case 25:
                        if item.red == True:
                            output += "<img src ='./static/image/image_mj/0m.gif'>"
                        else:
                            output += "<img src ='./static/image/image_mj/5m.gif'>"
                    case 26:
                        output += "<img src ='./static/image/image_mj/6m.gif'>"
                    case 27:
                        output += "<img src ='./static/image/image_mj/7m.gif'>"
                    case 28:
                        output += "<img src ='./static/image/image_mj/8m.gif'>"
                    case 29:
                        output += "<img src ='./static/image/image_mj/9m.gif'>"
                    case 31:
                        output += "<img src ='./static/image/image_mj/1p.gif'>"
                    case 32:
                        output += "<img src ='./static/image/image_mj/2p.gif'>"
                    case 33:
                        output += "<img src ='./static/image/image_mj/3p.gif'>"
                    case 34:
                        output += "<img src ='./static/image/image_mj/4p.gif'>"
                    case 35:
                        if item.red == True:
                            output += "<img src ='./static/image/image_mj/0p.gif'>"
                        else:
                            output += "<img src ='./static/image/image_mj/5p.gif'>"
                    case 36:
                        output += "<img src ='./static/image/image_mj/6p.gif'>"
                    case 37:
                        output += "<img src ='./static/image/image_mj/7p.gif'>"
                    case 38:
                        output += "<img src ='./static/image/image_mj/8p.gif'>"
                    case 39:
                        output += "<img src ='./static/image/image_mj/9p.gif'>"
                    case 41:
                        output += "<img src ='./static/image/image_mj/1z.gif'>"
                    case 44:
                        output += "<img src ='./static/image/image_mj/2z.gif'>"
                    case 47:
                        output += "<img src ='./static/image/image_mj/3z.gif'>"
                    case 50:
                        output += "<img src ='./static/image/image_mj/4z.gif'>"
                    case 53:
                        output += "<img src ='./static/image/image_mj/5z.gif'>"
                    case 56:
                        output += "<img src ='./static/image/image_mj/6z.gif'>"
                    case 59:
                        output += "<img src ='./static/image/image_mj/7z.gif'>"
                output += " " # 每一张牌的间隔
            output += "&nbsp;&nbsp;&nbsp;" # 每一组手牌、副露的间隔
        output += "<br>" # 牌型展示结束的换行

        # 2.输出符数组
        output += f"符数{i.m}符数组合为{i.m_combinations}<br>"

        # 3.输出番数组
        if "立直" in i.combinations_count:
            output += "立直 一番<br>"
        if "双立直" in i.combinations_count:
            output += "双立直 两番<br>"
        if "一发" in i.combinations_count:
            output += "一发 一番<br>"
        if "自摸" in i.combinations_count:
            output += "门前清自摸和 一番<br>"
        if "河底" in i.combinations_count:
            output += "河底捞鱼 一番<br>"
        if "海底" in i.combinations_count:
            output += "海底摸月 一番<br>"
        if "岭上" in i.combinations_count:
            output += "岭上开花 一番<br>"
        if "抢杠" in i.combinations_count:
            output += "抢杠 一番<br>"
        if "断幺" in i.combinations_count:
            output += "断幺 一番<br>"
        if "平和" in i.combinations_count:
            output += "平和 一番<br>"
        if "场风东" in i.combinations_count:
            output += "场风东 一番<br>"
        if "自风东" in i.combinations_count:
            output += "自风东 一番<br>"
        if "场风南" in i.combinations_count:
            output += "场风南 一番<br>"
        if "自风南" in i.combinations_count:
            output += "自风南 一番<br>"
        if "场风西" in i.combinations_count:
            output += "场风西 一番<br>"
        if "自风西" in i.combinations_count:
            output += "自风西 一番<br>"
        if "场风北" in i.combinations_count:
            output += "场风北 一番<br>"
        if "自风北" in i.combinations_count:
            output += "自风北 一番<br>"
        if "役牌中" in i.combinations_count:
            output += "役牌中 一番<br>"
        if "役牌白" in i.combinations_count:
            output += "役牌白 一番<br>"
        if "役牌发" in i.combinations_count:
            output += "役牌发 一番<br>"
        if "混全" in i.combinations_count:
            output += "混全带幺九 二番<br>"
        if "副露混全" in i.combinations_count:
            output += "副露混全 一番<br>"
        if "纯全" in i.combinations_count:
            output += "纯全带幺九 三番<br>"
        if "副露纯全" in i.combinations_count:
            output += "副露纯全 两番<br>"
        if "一杯口" in i.combinations_count:
            output += "一杯口 一番<br>"
        if "二杯口" in i.combinations_count:
            output += "二杯口 两番<br>"
        if "混一色" in i.combinations_count:
            output += "门前混一色 三番<br>"
        if "副露混一色" in i.combinations_count:
            output += "副露混一色 两番<br>"
        if "清一色" in i.combinations_count:
            output += "门前清一色 六番<br>"
        if "副露清一色" in i.combinations_count:
            output += "副露清一色 五番<br>"
        if "混老头" in i.combinations_count:
            output += "混老头 两番<br>"
        if "三色同顺" in i.combinations_count:
            output += "三色同顺 两番<br>"
        if "副露三色同顺" in i.combinations_count:
            output += "副露三色同顺 一番<br>"
        if "一气贯通" in i.combinations_count:
            output += "一气贯通 两番<br>"
        if "副露一气贯通" in i.combinations_count:
            output += "副露一气贯通 一番<br>"
        if "对对和" in i.combinations_count:
            output += "对对和 两番<br>"
        if "三色同刻" in i.combinations_count:
            output += f"三色同刻 两番<br>"
        if "三暗刻" in i.combinations_count:
            output += f"三暗刻 两番<br>"
        if "三杠子" in i.combinations_count:
            output += f"三杠子 两番<br>"
        if "小三元" in i.combinations_count:
            output += f"小三元 两番<br>"
        if "七对子" in i.combinations_count:
            output += f"七对子 两番<br>"
        dora_num = 0
        if Mj_input.dora_num:
            if int(Mj_input.dora_num) != 0 :
                dora_num += int(Mj_input.dora_num)
                output += f"宝牌 {Mj_input.dora_num}番<br>"
        if i.red_dora:
            output += f"赤宝牌 {i.red_dora}番<br>"
            dora_num += int(i.red_dora)
        if Mj_input.dora_num:
            if int(Mj_input.dora_num) != 0 :
                output += f"里宝牌 {Mj_input.deep_dora_num}番<br>"
                dora_num += int(Mj_input.deep_dora_num)
        # 4.输出计分组
        if i.m % 10 != 0:
            if i.m != 25:
                m_point = (10 - i.m % 10) + i.m
            else:
                m_point = i.m
        else:
            m_point = i.m
        if i.multiple_count - dora_num == 0:
            output += f"得分：无役 无法和牌<br>"
        else:
            output += f"得分：{m_point}符{i.multiple_count + dora_num}番{i.point_result}<br>"
    return output


#
#
#

if __name__ == "__main__" : # 主程序用于测试
    # 牌组计算阶段
    Mj_input = Mjobject()
    # 模拟前端传回 Mjobject 类 其中包含十项数据 ：
    Mj_input.hand = "406p406s999s东东东南南" # 手牌
    Mj_input.inputMPdata1 = "" # 副露1
    Mj_input.inputMPdata2 = "" # 副露2
    Mj_input.inputMPdata3 = "" # 副露3
    Mj_input.inputMPdata4 = "" # 副露4
    Mj_input.way_to_hepai = ["wayToHepaiZi"] # 和牌方式
    Mj_input.dora_num = "0" # 宝牌
    Mj_input.deep_dora_num = "0" # 里宝牌
    Mj_input.position_select = "positionOther" # 自风
    Mj_input.public_position_select = "publicPositionDong" # 场风

    # 通过majongdata 处理 Mj_input.hand 和 Mj_input.inputMPdata* 数据
    inputdata = majongdata(Mj_input.hand)
    print("原始牌组",inputdata)
    inputMPdata1 = majongdata(Mj_input.inputMPdata1)
    inputMPdata2 = majongdata(Mj_input.inputMPdata2)
    inputMPdata3 = majongdata(Mj_input.inputMPdata3)
    inputMPdata4 = majongdata(Mj_input.inputMPdata4)

    # MPcheck 将传入的副露数据合并至 inputdata.combinations_MP
    MPcheck(inputMPdata1,inputdata)  # 处理副露1
    MPcheck(inputMPdata2,inputdata)  # 处理副露2
    MPcheck(inputMPdata3,inputdata)  # 处理副露3
    MPcheck(inputMPdata4,inputdata)  # 处理副露4


    # 主程序:遍历所有可能的雀头状态 存储于alllist当中
    # ！包含冗余代码:此Paizucheck方法以及duizi,kezi,dazi三类check方法起初计划用于以单个算法同时完成向听数计算及番型得点计算的要求,因此包含向听计算等功能（即如果牌组向听数=0 则进行计番计算）
    # 但在目前实现中,flask应用中会对输入格式进行初步检测,确保输入满足计番格式需求;后续会使用一个单独的算法完成向听数计算以及待牌计算,如果通过,会将传参数据转发至mahjong_count。
    alllist = []
    inputdata.check() # 牌组方法自检
    alllist.extend(duizicheck(inputdata))
    print("以下是几种雀头可能", alllist)

    # 如果主牌组的roundnr不为0 (没有副露) 就进行七对子和国士无双的检测 也将结果存储在alllist当中
    if inputdata.roundnr == 0:
        QDcheck(inputdata,alllist)
        GScheck(inputdata,alllist)

    # handCheck方法将所有alllist中的牌组进行搭子和刻子的判断 如果同时满足两个条件则均进行判断 保证得出所有和牌可能性 直到alllist内不再有可以迭代的对象
    endlist = handCheck(alllist)

    # resultFilter方法遍历endlist,只保留向听数最近/和牌的牌组
    mahjonglist = resultFilter(endlist)

    # 去重 mahjonglist 中重复的牌组 牌组操作结束
    mahjonglist = distinctMjlist(mahjonglist)

    # 预处理 将对碰和牌的刻子移出手牌组合,放入副露组合
    if "wayToHepaiZi" not in Mj_input.way_to_hepai: # 如果不是自摸 荣和张如果构成暗刻 暗刻转为明刻
        for i in mahjonglist:
            if f"k{i.endhand}" in i.combinations:
                i.combinations.remove(f"k{i.endhand}")
                i.combinations_MP.append(f"k{i.endhand}")

    # 牌组计分阶段

    # 1.计算番数
    multple_count(mahjonglist, Mj_input, inputdata)

    # 2.计算符数
    m_count(mahjonglist, Mj_input)

    # 3.计算得点
    point_count(mahjonglist, Mj_input)

    # 按得点排序
    mahjonglist = sorted(mahjonglist, key=lambda i: i.point_count, reverse=True)

    # 打印番数、符数、得点
    for i in mahjonglist:
        print(f"手牌:{i.combinations}副露:{i.combinations_MP}番种:{i.combinations_count}含宝牌番数为:{i.multiple_count}")
        print(f"手牌:{i.combinations}副露:{i.combinations_MP}符数种为：{i.m_combinations}符数:{i.m}")
        print(f"得分：{i.point_result}")

    # 根据番数、符数、得点返回前端结果
    output = multple_count_output(mahjonglist, Mj_input, inputdata, inputMPdata1, inputMPdata2, inputMPdata3,inputMPdata4)
    print(output)

# 主程序副本 供外部调用
def mahjong_count(Mj_input):
    # 通过majongdata 处理 Mj_input.hand 和 Mj_input.inputMPdata* 数据
    inputdata = majongdata(Mj_input.hand)

    print("原始牌组",inputdata)
    inputMPdata1 = majongdata(Mj_input.inputMPdata1)
    inputMPdata2 = majongdata(Mj_input.inputMPdata2)
    inputMPdata3 = majongdata(Mj_input.inputMPdata3)
    inputMPdata4 = majongdata(Mj_input.inputMPdata4)

    # MPcheck 将传入的副露数据合并至 inputdata.combinations_MP
    MPcheck(inputMPdata1,inputdata)  # 处理副露1
    MPcheck(inputMPdata2,inputdata)  # 处理副露2
    MPcheck(inputMPdata3,inputdata)  # 处理副露3
    MPcheck(inputMPdata4,inputdata)  # 处理副露4

    # 主程序:遍历所有可能的雀头状态 存储于alllist当中
    # ！包含冗余代码:此Paizucheck方法以及duizi,kezi,dazi三类check方法起初计划用于以单个算法同时完成向听数计算及番型得点计算的要求,因此包含向听计算等功能（即如果牌组向听数=0 则进行计番计算）
    # 但在目前实现中,flask应用中会对输入格式进行初步检测,确保输入满足计番格式需求;后续会使用一个单独的算法完成向听数计算以及待牌计算,如果通过,会将传参数据转发至mahjong_count。
    alllist = []
    inputdata.check()  # 牌组方法自检
    alllist.extend(duizicheck(inputdata))
    print("以下是几种雀头可能", alllist)

    # 如果主牌组的roundnr不为0 (没有副露) 就进行七对子和国士无双的检测 也将结果存储在alllist当中
    if inputdata.roundnr == 0:
        QDcheck(inputdata, alllist)
        GScheck(inputdata, alllist)

    # handCheck方法将所有alllist中的牌组进行搭子和刻子的判断 如果同时满足两个条件则均进行判断 保证得出所有和牌可能性 直到alllist内不再有可以迭代的对象
    endlist = handCheck(alllist)

    # resultFilter方法遍历endlist,只保留向听数最近/和牌的牌组
    mahjonglist = resultFilter(endlist)

    # 去重 mahjonglist 中重复的牌组 牌组操作结束
    mahjonglist = distinctMjlist(mahjonglist)

    # 预处理 将对碰和牌的刻子移出手牌组合,放入副露组合
    if "wayToHepaiZi" not in Mj_input.way_to_hepai:  # 如果不是自摸 荣和张如果构成暗刻 暗刻转为明刻
        for i in mahjonglist:
            if f"k{i.endhand}" in i.combinations:
                i.combinations.remove(f"k{i.endhand}")
                i.combinations_MP.append(f"k{i.endhand}")

    # 牌组计分阶段

    # 1.计算番数
    multple_count(mahjonglist, Mj_input, inputdata)

    # 2.计算符数
    m_count(mahjonglist, Mj_input)

    # 3.计算得点
    point_count(mahjonglist, Mj_input)

    # 按得点排序
    mahjonglist = sorted(mahjonglist, key=lambda i: i.point_count, reverse=True)

    # 打印番数、符数、得点
    for i in mahjonglist:
        print(f"手牌:{i.combinations}副露:{i.combinations_MP}番种:{i.combinations_count}含宝牌番数为:{i.multiple_count}")
        print(f"手牌:{i.combinations}副露:{i.combinations_MP}符数种为：{i.m_combinations}符数:{i.m}")
        print(f"得分：{i.point_result}")

    # 根据番数、符数、得点返回前端结果
    output = multple_count_output(mahjonglist, Mj_input, inputdata, inputMPdata1, inputMPdata2, inputMPdata3,inputMPdata4)
    return output

