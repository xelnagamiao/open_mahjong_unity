# 本文件用于检测手牌向听数和向听待牌 是计番程序的青春版
# 感谢csdn博主扑克子对向听数检测方法提出的新思路 博客链接：https://blog.csdn.net/qq_44637852/article/details/104171646?spm=1001.2014.3001.5502
# 简易牌组类
from mahjong import *

def result_filter(endlist):
    mjlist = []
    for i in endlist:
        if i.roundnr >=11:
            i.roundnr = 14 - i.roundnr
            mjlist.append(i)
    for i in mjlist:
        print("最低向听数为",i.roundnr,"包含的牌组包括",i.combinations,"副露牌组包括",i.combinations_MP,"剩余的牌包括",i)
    return mjlist

# 调试用主程序
if __name__ == "__main__" : # 主程序用于测试
    # 主程序片段 见mahjong.py mahjong_count方法
    inputdata = majongdata("123123123s99发发s")
    alllist = []
    inputdata.check() # 牌组方法自检
    alllist.extend(duizicheck(inputdata))
    print("以下是几种雀头可能", alllist)
    if inputdata.roundnr == 0:
        QDcheck(inputdata,alllist)
        GScheck(inputdata,alllist)
    endlist = handCheck(alllist)

    # 保留全部3向听和2向听的牌组
    mahjonglist = result_filter(endlist)

    # 传入值为13张 向听数存在三种可能
    # 1.roundnr == 3 [缺面子搭子]
    # 2.roundnr == 2 [缺雀头] [七对子]
    # 3.roundnr >= 4 [未听牌]

    # 其中roundnr == 3 也有两种情况
    # 1.能构成坎、好型、对碰听牌的
    # 2.不能构成的 [未听牌]

    roundnr = mahjonglist[0].roundnr # 同步向听数
    lack_tile = set() # 创建听牌可能集合
    output = "" # 创建返回值

    for i in mahjonglist:
        # [缺面子搭子]
        if i.roundnr == 3:
            for item in i: # [13,14] [13,15] [13,13]
                if item + 1 in i: # [13,14] # 检测两面
                    lack_tile.add(item - 1)
                    lack_tile.add(item + 2)
                elif item + 2 in i: # [13,15] # 检测坎张
                    lack_tile.add(item + 1)
                elif i.count(item) == 2: # [13,13] 检测对碰
                    lack_tile.add(item)
        # [缺雀头] [七对子]
        elif i.roundnr == 2:
            for item in i: # [13]
                lack_tile.add(item)
        # 向听数异常
        else:
            output = "手牌异常，请联系网站管理员q1448826180"

    # 没有达到向听数的牌组和没有组成坎好型对碰的未听牌
    if not mahjonglist:
        output = "手牌未听牌"
    elif not lack_tile:
        output = "手牌未听牌"

    if output != "手牌未听牌" :
        # 打印[手牌图片]
        for item in inputdata:
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
            output += " "  # 每一张牌的间隔
        output += "<br>"

        # 打印[听牌文字]
        lack_tile = sorted(list(lack_tile))
        lack_tile_name = ""
        for item in lack_tile:
            match item:
                case 11:
                    lack_tile_name += "一索、"
                case 12:
                    lack_tile_name += "二索、"
                case 13:
                    lack_tile_name += "三索、"
                case 14:
                    lack_tile_name += "四索、"
                case 15:
                    lack_tile_name += "五索、"
                case 16:
                    lack_tile_name += "六索、"
                case 17:
                    lack_tile_name += "七索、"
                case 18:
                    lack_tile_name += "八索、"
                case 19:
                    lack_tile_name += "九索、"
                case 21:
                    lack_tile_name += "一万、"
                case 22:
                    lack_tile_name += "二万、"
                case 23:
                    lack_tile_name += "三万、"
                case 24:
                    lack_tile_name += "四万、"
                case 25:
                    lack_tile_name += "五万、"
                case 26:
                    lack_tile_name += "六万、"
                case 27:
                    lack_tile_name += "七万、"
                case 28:
                    lack_tile_name += "八万、"
                case 29:
                    lack_tile_name += "九万、"
                case 31:
                    lack_tile_name += "一筒、"
                case 32:
                    lack_tile_name += "二筒、"
                case 33:
                    lack_tile_name += "三筒、"
                case 34:
                    lack_tile_name += "四筒、"
                case 35:
                    lack_tile_name += "五筒、"
                case 36:
                    lack_tile_name += "六筒、"
                case 37:
                    lack_tile_name += "七筒、"
                case 38:
                    lack_tile_name += "八筒、"
                case 39:
                    lack_tile_name += "九筒、"
                case 41:
                    lack_tile_name += "东、"
                case 44:
                    lack_tile_name += "南、"
                case 47:
                    lack_tile_name += "西、"
                case 50:
                    lack_tile_name += "北、"
                case 53:
                    lack_tile_name += "白、"
                case 56:
                    lack_tile_name += "发、"
                case 59:
                    lack_tile_name += "中、"
        # 打印 听牌描述-> [听牌文字] <-
        lack_tile_name = lack_tile_name[:-1] # 去除最后一个逗号
        output += f"手牌已听牌，听牌为：{lack_tile_name}<br>"
        # 打印[听牌图片]
        for item in lack_tile:
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
            output += " "  # 每一张牌的间隔

    # 输出结果
    print(lack_tile)
    print(output)

# 外部引用方法
def xt_count(hand):
    # 主程序片段 见mahjong.py mahjong_count方法
    inputdata = majongdata(hand)
    alllist = []
    inputdata.check() # 牌组方法自检
    alllist.extend(duizicheck(inputdata))
    print("以下是几种雀头可能", alllist)
    if inputdata.roundnr == 0:
        QDcheck(inputdata,alllist)
        GScheck(inputdata,alllist)
    endlist = handCheck(alllist)

    # 保留全部3向听和2向听的牌组
    mahjonglist = result_filter(endlist)

    # 传入值为13张 向听数存在三种可能
    # 1.roundnr == 3 [缺面子搭子]
    # 2.roundnr == 2 [缺雀头] [七对子]
    # 3.roundnr >= 4 [未听牌]

    # 其中roundnr == 3 也有两种情况
    # 1.能构成坎、好型、对碰听牌的
    # 2.不能构成的 [未听牌]

    roundnr = mahjonglist[0].roundnr # 同步向听数
    lack_tile = set() # 创建听牌可能集合
    output = "" # 创建返回值

    for i in mahjonglist:
        # [缺面子搭子]
        if i.roundnr == 3:
            for item in i: # [13,14] [13,15] [13,13]
                if item + 1 in i: # [13,14] # 检测两面
                    lack_tile.add(item - 1)
                    lack_tile.add(item + 2)
                elif item + 2 in i: # [13,15] # 检测坎张
                    lack_tile.add(item + 1)
                elif i.count(item) == 2: # [13,13] 检测对碰
                    lack_tile.add(item)
        # [缺雀头] [七对子]
        elif i.roundnr == 2:
            for item in i: # [13]
                lack_tile.add(item)
        # 向听数异常
        else:
            output = "手牌异常，请联系网站管理员q1448826180"

    # 没有达到向听数的牌组和没有组成坎好型对碰的未听牌
    if not mahjonglist:
        output = "手牌未听牌"
    elif not lack_tile:
        output = "手牌未听牌"

    if output != "手牌未听牌" :
        # 打印[手牌图片]
        for item in inputdata:
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
            output += " "  # 每一张牌的间隔
        output += "<br>"

        # 打印[听牌文字]
        lack_tile = sorted(list(lack_tile))
        lack_tile_name = ""
        for item in lack_tile:
            match item:
                case 11:
                    lack_tile_name += "一索、"
                case 12:
                    lack_tile_name += "二索、"
                case 13:
                    lack_tile_name += "三索、"
                case 14:
                    lack_tile_name += "四索、"
                case 15:
                    lack_tile_name += "五索、"
                case 16:
                    lack_tile_name += "六索、"
                case 17:
                    lack_tile_name += "七索、"
                case 18:
                    lack_tile_name += "八索、"
                case 19:
                    lack_tile_name += "九索、"
                case 21:
                    lack_tile_name += "一万、"
                case 22:
                    lack_tile_name += "二万、"
                case 23:
                    lack_tile_name += "三万、"
                case 24:
                    lack_tile_name += "四万、"
                case 25:
                    lack_tile_name += "五万、"
                case 26:
                    lack_tile_name += "六万、"
                case 27:
                    lack_tile_name += "七万、"
                case 28:
                    lack_tile_name += "八万、"
                case 29:
                    lack_tile_name += "九万、"
                case 31:
                    lack_tile_name += "一筒、"
                case 32:
                    lack_tile_name += "二筒、"
                case 33:
                    lack_tile_name += "三筒、"
                case 34:
                    lack_tile_name += "四筒、"
                case 35:
                    lack_tile_name += "五筒、"
                case 36:
                    lack_tile_name += "六筒、"
                case 37:
                    lack_tile_name += "七筒、"
                case 38:
                    lack_tile_name += "八筒、"
                case 39:
                    lack_tile_name += "九筒、"
                case 41:
                    lack_tile_name += "东、"
                case 44:
                    lack_tile_name += "南、"
                case 47:
                    lack_tile_name += "西、"
                case 50:
                    lack_tile_name += "北、"
                case 53:
                    lack_tile_name += "白、"
                case 56:
                    lack_tile_name += "发、"
                case 59:
                    lack_tile_name += "中、"
        # 打印 听牌描述-> [听牌文字] <-
        lack_tile_name = lack_tile_name[:-1] # 去除最后一个逗号
        output += f"手牌已听牌，听牌为：{lack_tile_name}<br>"
        # 打印[听牌图片]
        for item in lack_tile:
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
            output += " "  # 每一张牌的间隔

    # 返回输出结果
    return output




""" 废弃方法
class Paizu(list):
    def __init__(self, pais=None):
        super().__init__()
        if pais is None: # 传值为空则建立空列表
            pais = []
        self.extend(pais)  # 有传值则通过传值赋值自身列表
        self.roundnr = 0 # 向听数
        self.QTget = False
# 阉割mahjongdata
def mahjongdata(data):
    # 建立字牌匹配集
    savewordclass={"东", "南", "西", "北","发","白","中"}
    # 建立暂存不同牌组的字符串
    saves = ""
    savem = ""
    savep = ""
    # 输出值
    savenumber = ""
    saveword = ""
    for i in data:
        # 数字进入savenumber暂存字符串 字牌进入saveword暂存字符串
        if i.isdigit():
            savenumber+=i
        elif i in savewordclass:
            saveword+=i
        # 当遇到牌组标签s m p 时,将暂存的数据放入对应的标签集中
        elif i == "s":
            saves += savenumber
            savenumber= ""
        elif i == "m":
            savem += savenumber
            savenumber= ""
        elif i == "p":
            savep += savenumber
            savenumber = ""
        # 出现不匹配的值进行报错并且跳出
        else:
            print(f"请勿输入超出数字,字母's','m','p'以及东南西北白发中以外的字符")
            break
        # 获得 saves,savem,savep,saveword 下一步进行合并
    mjsave = Paizu()
    # 将四个集合中的数据存储于mjsave中
    for i in saves:
        mjsave.append(int(i)+10)
    for i in savem:
        mjsave.append(int(i)+20)
    for i in savep:
        mjsave.append(int(i)+30)
    for char in saveword:
        match char:
            case "东":
                mjsave.append(41)
            case "南":
                mjsave.append(44)
            case "西":
                mjsave.append(47)
            case "北":
                mjsave.append(50)
            case "白":
                mjsave.append(53)
            case "发":
                mjsave.append(56)
            case "中":
                mjsave.append(59)
    return mjsave # 返回牌组mjsave
# 极简雀头检测
def quetoucheck(mjlist):
    same_tiles = 0
    for i in mjlist:
        # 如果相同牌大于等于2，并且没有被剔除过，进入循环。
        if mjlist.count(i) >= 2 and i != same_tiles :
            same_tiles = i
            skip_tiles = 2
            output_list = Paizu()
            #
            for item in mjlist:
                if item == same_tiles and skip_tiles != 0 :
                    skip_tiles -= 1
                else:
                    output_list.append(item)
            output_list.QTget = True
            output_list.roundnr +=2
            alllist.append(output_list)
    alllist.append(mjlist) # 最后将未遍历雀头的原牌组放入牌组的末尾
# 青春面子检测
def mianzicheck(mjlist):
    for i in mjlist: # 获取列表
        #
        #
        #
        for item in i : # 获取列表i的元素item
            if i.count(item) >=3 : # 如果超过3个
                i.remove(item)
                i.remove(item)
                i.remove(item)
                i.roundnr += 3
        for item in i:
            print(i,item)
            if i.count(item) >= 3:
                for element in i:
                    same_tiles = i
                    skip_tiles = 2
                    if item == same_tiles:
if __name__ == '__main__':
    alllist = []
    inputdata = mahjongdata("4m4666p3338s23567p")
    print(inputdata)
    # 遍历雀头可能
    quetoucheck(inputdata)
    print("以下是几种雀头可能", alllist)
    # 遍历完整型可能
    mianzicheck(alllist)
    endlist = handCheck(alllist)
    for i in endlist:
        for item in i:
            # 去除面子不完整型（两面）
            if item+2 in i:
                i.remove(item)
                i.remove(item+2)
                i.roundnr += 1
                break
            elif item-2 in i:
                i.remove(item)
                i.remove(item-2)
                i.roundnr += 1
                break
    print(endlist)
    for i in endlist:
        print(i.roundnr)
"""