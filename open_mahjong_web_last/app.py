# 导入 Flask 框架，用于创建和配置 Web 应用
# 导入 SQLAlchemy，用于 ORM（对象关系映射），方便操作数据库
# 从 mahjong 模块中导入 mahjong_count 函数和 Mjobject 类，用于麻将计分和对象操作
from flask import Flask,render_template,request
from flask_sqlalchemy import SQLAlchemy
from mahjong import mahjong_count,Mjobject
from count_xt import xt_count

# 创建app变量 传参flask对象 __name__ = __main__  使用__name__参数 使flask类所调用的库的根目录确定为app.py本文件所在的目录
app = Flask(__name__)

# 配置web
app.config['DEBUG'] = True  # 开启调试模式
app.config['HOST'] = '127.0.0.2'  # 主机地址
app.config['PORT'] = 5000  # 端口号

# 配置数据库
# app.config['SQLALCHEMY_DATABASE_URI'] = 'mysql+pymysql://mahjong_fit:$$$$$$$$@localhost:3306/mahjong_fit' # 服务器配置
app.config['SQLALCHEMY_DATABASE_URI'] = 'mysql+pymysql://root:qwe123@localhost:3306/database_mj' # 本地测试数据库
app.config['SQLALCHEMY_TRACK_MODIFICATIONS'] = False # 不追踪修改 节约性能
db = SQLAlchemy(app) # 绑定对象实例

# 定义数据库
class MahjongResult(db.Model):
    id = db.Column(db.Integer, primary_key=True) # 主键
    mj_input = db.Column(db.String(200)) # 字符串
    mj_output = db.Column(db.Text) # 字符串
    is_valid = db.Column(db.Boolean, nullable=False)  # 布尔值，不允许为空
    created_at = db.Column(db.DateTime, default=db.func.current_timestamp()) # 时间戳
    def __repr__(self):
        return f'<MahjongResult {self.id}>'

# 创建数据库表 如有则使用
with app.app_context():
    db.create_all()

# 立直麻将对数据库返回值
def return_result(mj_input,output,is_valid):
    result = MahjongResult(mj_input=mj_input, mj_output=output, is_valid=is_valid)
    db.session.add(result)
    db.session.commit()

#——————————————————————————————————————————————————————————————————————————————————————————————————————————#

@app.route("/",methods=["GET","POST","PUT"])
@app.route("/index",methods=["GET","POST","PUT"])
def home():
    return render_template("index.html")

@app.route("/mahjong_XT",methods=["GET","POST","PUT"])
def xt_page():
    output = ""
    return render_template("mahjong_XT.html",output=output)
@app.route("/count_hand",methods=["GET","POST","PUT"])
def xt_hand_count():
    hand = request.form.get('hand')
    count_tiles = 0
    allow_character = {"0","1","2","3","4","5","6","7","8","9","0","s","m","p","东","南","西","北","中","白","发"} # 输入字符串限制
    count_character = {"0","1","2","3","4","5","6","7","8","9","0","东","南","西","北","中","白","发"} # 被计入麻将牌的各字符
    # 1.输入超出字符串限制
    for i in hand:
        if i not in allow_character:
            output = "格式错误:手牌中不得出现超出0,1,2,3,4,5,6,7,8,9,0,s,m,p,东,南,西,北的字符"
            return render_template("mahjong_XT.html",output = output)
    # 2.传入手牌超出或不足14枚
    for i in hand:
        if i in count_character:
            count_tiles += 1
    if count_tiles != 13:
        if count_tiles > 13: # 大于13
            output = "格式错误:传入麻将牌数量大于13"
            return render_template("mahjong_XT.html",output=output)
        else: # 小于13
            output = "格式错误:传入麻将牌数量小于13"
            return render_template("mahjong_XT.html",output=output)
    output = xt_count(hand)
    return render_template("mahjong_XT.html",output=output)

@app.route("/mahjong_GB",methods=["GET","POST","PUT"])
def gb_page(): # 开发中
    return render_template("mahjong_GB.html")

# count_page 用于导向立直麻将解析界面 get_count 用于返回立直麻将解析界面结果值
@app.route("/mahjong_RC",methods=["GET","POST","PUT"])
def count_page():
    output = ""
    return render_template("mahjong_RC.html",output = output)
@app.route("/count",methods=["POST"])
def get_count():
    Mj_count = "通信中"
    Mahjong_hand = Mjobject() # 获取数据
    Mahjong_hand.hand = request.form.get('hand')
    Mahjong_hand.inputMPdata1 = request.form.get('fulu1')
    Mahjong_hand.inputMPdata2 = request.form.get('fulu2')
    Mahjong_hand.inputMPdata3 = request.form.get('fulu3')
    Mahjong_hand.inputMPdata4 = request.form.get('fulu4')
    Mahjong_hand.way_to_hepai = request.form.getlist('wayToHepai')
    Mahjong_hand.dora_num = request.form.get('doraNum')
    Mahjong_hand.deep_dora_num = request.form.get('deepDoraNum')
    Mahjong_hand.position_select = request.form.get("positionSelect")
    Mahjong_hand.public_position_select = request.form.get("publicPositionSelect")
    # 如果和牌方式是河底或抢杠 属于荣和型 如果和牌方式属于海底或岭上 属于自摸型
    if "wayToHepaiHe" in Mahjong_hand.way_to_hepai or "wayToHepaiQG" in Mahjong_hand.way_to_hepai:
        Mahjong_hand.way_to_hepai.append("wayToHePaiRo")
    if "wayToHepaiHai" in Mahjong_hand.way_to_hepai or "wayToHepaiLin" in Mahjong_hand.way_to_hepai:
        Mahjong_hand.way_to_hepai.append("wayToHePaiZi")

    print("收到信息：") # 显示数据
    print("手牌：",Mahjong_hand.hand)
    print("副露1：",Mahjong_hand.inputMPdata1)
    print("副露2：",Mahjong_hand.inputMPdata2)
    print("副露3：",Mahjong_hand.inputMPdata3)
    print("副露4：",Mahjong_hand.inputMPdata4)
    print("和牌手段",Mahjong_hand.way_to_hepai) # 可以为空
    print("宝牌数：",Mahjong_hand.dora_num) # 可以为空
    print("里宝牌数：",Mahjong_hand.deep_dora_num) # 可以为空
    print("自风：",Mahjong_hand.position_select) # 无限制
    print("场风：",Mahjong_hand.public_position_select) # 无限制

    # 检测输入
    # 四种报错 1.超出规定字符串集合限制 2.超出副露组合限制 3.牌组超出或不足规定长度限制 4.宝牌和里宝牌不为阿拉伯数字 如牌组未听牌或无役则按正常结果返回
    mj_input = (f"手牌：{Mahjong_hand.hand}副露1：{Mahjong_hand.inputMPdata1}副露2：{Mahjong_hand.inputMPdata2}副露3：{Mahjong_hand.inputMPdata3}副露4：{Mahjong_hand.inputMPdata4}和牌手段:{Mahjong_hand.way_to_hepai}宝牌数：{Mahjong_hand.dora_num}里宝牌数：{Mahjong_hand.deep_dora_num}场风：{Mahjong_hand.public_position_select}")
    is_valid = True
    allow_character = {"0","1","2","3","4","5","6","7","8","9","0","s","m","p","东","南","西","北","中","白","发"} # 输入字符串限制
    count_character = {"0","1","2","3","4","5","6","7","8","9","0","东","南","西","北","中","白","发"} # 被计入麻将牌的各字符
    count_tiles = 0 # 麻将牌数量 应当满足14
    allow_MP = {"123s","123m","123p","234s","234m","234p","345s","345m","345p", # 副露组合限制
                "456s","456m","456p","567s","567m","567p","678s","678m","678p","789s","789m","789p",
                "340s", "340m", "340p","406s", "406m", "406p", "067s", "067m", "067p",
                "111s","1111s","11111s","111m","1111m","11111m","111p","1111p","11111p",
                "222s","2222s","22222s","222m","2222m","22222m","222p","2222p","22222p",
                "333s","3333s","33333s","333m","3333m","33333m","333p","3333p","33333p",
                "444s","4444s","44444s","444m","4444m","44444m","444p","4444p","44444p",
                "555s","5555s","55555s","555m","5555m","55555m","555p","5555p","55555p",
                "666s","6666s","66666s","666m","6666m","66666m","666p","6666p","66666p",
                "777s","7777s","77777s","777m","7777m","77777m","777p","7777p","77777p",
                "888s","8888s","88888s","888m","8888m","88888m","888p","8888p","88888p",
                "999s","9999s","99999s","999m","9999m","99999m","999p","9999p","99999p",
                "东东东","东东东东","东东东东东","南南南","南南南南","南南南南南","西西西","西西西西","西西西西西","北北北","北北北北","北北北北北",
                "中中中","中中中中","中中中中中","发发发","发发发发","发发发发发","白白白","白白白白","白白白白白",}


    # 1.如果传入手牌以及副露的手牌不符合规则即报错
    for i in Mahjong_hand.hand + Mahjong_hand.inputMPdata1 + Mahjong_hand.inputMPdata2 + Mahjong_hand.inputMPdata3 + Mahjong_hand.inputMPdata4:
        if i not in allow_character:
            output = "格式错误:手牌与副露中不得出现超出0,1,2,3,4,5,6,7,8,9,0,s,m,p,东,南,西,北的字符"
            is_valid = False
            return_result(mj_input=mj_input, output=output, is_valid=is_valid)
            return render_template("mahjong_RC.html", Mj_count=Mj_count, output = output)

    # 2.如果传入的副露不为空并且不符合allow_MP:中的组合即报错
    MP_list = [Mahjong_hand.inputMPdata1,Mahjong_hand.inputMPdata2,Mahjong_hand.inputMPdata3,Mahjong_hand.inputMPdata4]
    for i in MP_list:
        if i :
            count_tiles += 3
            if i not in allow_MP:
                output = f"格式错误:副露中只能出现{allow_MP}内的组合"
                is_valid = False
                return_result(mj_input=mj_input, output=output, is_valid=is_valid)
                return render_template("mahjong_RC.html", Mj_count=Mj_count, output=output)

    # 3.如果传入的副露加上手牌超出不足14枚则报错
    for i in Mahjong_hand.hand:
        if i in count_character:
            count_tiles += 1
    if count_tiles != 14:
        if count_tiles > 14: # 大于14
            output = "格式错误:传入麻将牌数量大于14"
            is_valid = False
            return_result(mj_input=mj_input, output=output, is_valid=is_valid)
            return render_template("mahjong_RC.html", Mj_count=Mj_count, output=output)
        else: # 小于14
            output = "格式错误:传入麻将牌数量小于14"
            is_valid = False
            return_result(mj_input=mj_input, output=output, is_valid=is_valid)
            return render_template("mahjong_RC.html", Mj_count=Mj_count, output=output)

    # 4.如果宝牌和里宝牌的输入不为阿拉伯数字则报错
    if Mahjong_hand.deep_dora_num:
        if not Mahjong_hand.deep_dora_num.isdigit():
            output = "格式错误:宝牌和里宝牌应当为阿拉伯数字"
            is_valid = False
            return_result(mj_input=mj_input, output=output, is_valid=is_valid)
            return render_template("mahjong_RC.html", Mj_count=Mj_count, output=output)
    if Mahjong_hand.dora_num:
        if not Mahjong_hand.dora_num.isdigit():
            output = "格式错误:宝牌和里宝牌应当为阿拉伯数字"
            is_valid = False
            return_result(mj_input=mj_input, output=output, is_valid=is_valid)
            return render_template("mahjong_RC.html", Mj_count=Mj_count, output=output)

    # 计算输出
    try:
        output = mahjong_count(Mahjong_hand) # mahjong_count 主程序
    except Exception as count_error: #
        output = f"计算错误:主程序运算出错,error_name = {count_error},请联系网站管理员q1448826180"
        is_valid = False
        return_result(mj_input=mj_input, output=output, is_valid=is_valid)
        return render_template("mahjong_RC.html", Mj_count=Mj_count, output=output)

    print("返回信息:",Mj_count)
    return_result(mj_input=mj_input, output=output, is_valid=is_valid)
    return render_template("mahjong_RC.html",Mj_count = Mj_count,output = output)

if __name__ == "__main__" :
    app.run(host=app.config['HOST'], port=app.config['PORT'], debug=app.config['DEBUG'])

