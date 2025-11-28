## 数据库类型
PostgreSQL

## 数据库连接配置
- 主机: localhost
- 端口: 5432
- 数据库名: open_mahjong
- 默认用户: postgres

## 数据库表结构

### users 用户信息表 存储玩家账号信息
| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGSERIAL | PRIMARY KEY | 用户唯一标识，从 10000000 开始自增 |
| username | VARCHAR(255) | UNIQUE NOT NULL | 用户名，唯一 |
| password | VARCHAR(255) | NOT NULL | 密码哈希值（格式：salt:hash，使用 SHA256 哈希） |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |

### user_config 用户设置
voice 音量
profile image 头像
character 选择角色
voice_type 选择音色



### game_records 存储对局记录的牌谱
| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| game_id | BIGSERIAL | PRIMARY KEY | 对局唯一标识，自增 |
| record | JSONB | NOT NULL | 完整的牌谱记录（包含 game_title 和 game_round） |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |

### game_player_records 牌谱记录表，用于存储玩家对局记录
| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| game_id | BIGINT | PRIMARY KEY, REFERENCES game_records(game_id) ON DELETE CASCADE | 对局ID，外键关联 game_records |
| user_id | BIGINT | PRIMARY KEY, REFERENCES users(user_id) ON DELETE CASCADE | 用户ID，外键关联 users |
| username | VARCHAR(255) | NOT NULL | 玩家用户名（对局时的用户名） |
| score | INT | NOT NULL | 玩家最终分数（可能为负数） |
| rank | INT | NOT NULL CHECK (rank >= 1 AND rank <= 4) | 最终排名（1=一位，2=二位，3=三位，4=四位） |
| rule | VARCHAR(10) | NOT NULL | 规则类型（GB=国标，JP=日麻） |
| character_used | VARCHAR(255) | NULL | 使用的角色（可为空） |



### 客户端在数据头部会显示国标麻将总数据 以下为子项
### gb_record_4/4_game 全庄
### gb_record_3/4_game 西风战
### gb_record_2/4_game 南风战
### gb_record_1/4_game 东风战
### gb_record_4/4_game_ladder 全庄战排位
### gb_record_2/4_game_ladder 半庄战排位
| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| total_games | INT | NOT NULL DEFAULT 0 | 总游戏局数 |
| total_rounds | INT | NOT NULL DEFAULT 0 | 总游戏回合数 |
| win_count | INT | NOT NULL DEFAULT 0 | 和牌总次数 |
| self_draw_count | INT | NOT NULL DEFAULT 0 | 自摸总次数 |
| deal_in_count | INT | NOT NULL DEFAULT 0 | 放铳总次数 |
| total_fan_score | INT | NOT NULL DEFAULT 0 | 总和牌番数（用于计算平均番数：total_fan_score / win_count） |
| total_win_turn | INT | NOT NULL DEFAULT 0 | 总和牌巡目（用于计算平均和巡：total_win_turn / win_count） |
| total_fangchong_score | INT | NOT NULL DEFAULT 0 | 总放铳分数（铳点） |
| first_place_count | INT | NOT NULL DEFAULT 0 | 一位次数 |
| second_place_count | INT | NOT NULL DEFAULT 0 | 二位次数 |
| third_place_count | INT | NOT NULL DEFAULT 0 | 三位次数 |
| fourth_place_count | INT | NOT NULL DEFAULT 0 | 四位次数 |
### gb_record_fan_count 国标番种达成次数
| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| dasixi | INT | NOT NULL DEFAULT 0 | 大四喜次数 |
| dasanyuan | INT | NOT NULL DEFAULT 0 | 大三元次数 |
| lvyise | INT | NOT NULL DEFAULT 0 | 绿一色次数 |
| jiulianbaodeng | INT | NOT NULL DEFAULT 0 | 九莲宝灯次数 |
| sigang | INT | NOT NULL DEFAULT 0 | 四杠次数 |
| sangang | INT | NOT NULL DEFAULT 0 | 三杠次数 |
| lianqidui | INT | NOT NULL DEFAULT 0 | 连七对次数 |
| shisanyao | INT | NOT NULL DEFAULT 0 | 十三幺次数 |
| qingyaojiu | INT | NOT NULL DEFAULT 0 | 清幺九次数 |
| xiaosixi | INT | NOT NULL DEFAULT 0 | 小四喜次数 |
| xiaosanyuan | INT | NOT NULL DEFAULT 0 | 小三元次数 |
| ziyise | INT | NOT NULL DEFAULT 0 | 字一色次数 |
| sianke | INT | NOT NULL DEFAULT 0 | 四暗刻次数 |
| yiseshuanglonghui | INT | NOT NULL DEFAULT 0 | 一色双龙会次数 |
| yisesitongshun | INT | NOT NULL DEFAULT 0 | 一色四同顺次数 |
| yisesijiegao | INT | NOT NULL DEFAULT 0 | 一色四节高次数 |
| yisesibugao | INT | NOT NULL DEFAULT 0 | 一色四步高次数 |
| hunyaojiu | INT | NOT NULL DEFAULT 0 | 混幺九次数 |
| qiduizi | INT | NOT NULL DEFAULT 0 | 七对子次数 |
| qixingbukao | INT | NOT NULL DEFAULT 0 | 七星不靠次数 |
| quanshuangke | INT | NOT NULL DEFAULT 0 | 全双刻次数 |
| qingyise | INT | NOT NULL DEFAULT 0 | 清一色次数 |
| yisesantongshun | INT | NOT NULL DEFAULT 0 | 一色三同顺次数 |
| yisesanjiegao | INT | NOT NULL DEFAULT 0 | 一色三节高次数 |
| quanda | INT | NOT NULL DEFAULT 0 | 全大次数 |
| quanzhong | INT | NOT NULL DEFAULT 0 | 全中次数 |
| quanxiao | INT | NOT NULL DEFAULT 0 | 全小次数 |
| qinglong | INT | NOT NULL DEFAULT 0 | 清龙次数 |
| sanseshuanglonghui | INT | NOT NULL DEFAULT 0 | 三色双龙会次数 |
| yisesanbugao | INT | NOT NULL DEFAULT 0 | 一色三步高次数 |
| quandaiwu | INT | NOT NULL DEFAULT 0 | 全带五次数 |
| santongke | INT | NOT NULL DEFAULT 0 | 三同刻次数 |
| sananke | INT | NOT NULL DEFAULT 0 | 三暗刻次数 |
| quanbukao | INT | NOT NULL DEFAULT 0 | 全不靠次数 |
| zuhelong | INT | NOT NULL DEFAULT 0 | 组合龙次数 |
| dayuwu | INT | NOT NULL DEFAULT 0 | 大于五次数 |
| xiaoyuwu | INT | NOT NULL DEFAULT 0 | 小于五次数 |
| sanfengke | INT | NOT NULL DEFAULT 0 | 三风刻次数 |
| hualong | INT | NOT NULL DEFAULT 0 | 花龙次数 |
| tuibudao | INT | NOT NULL DEFAULT 0 | 推不倒次数 |
| sansesantongshun | INT | NOT NULL DEFAULT 0 | 三色三同顺次数 |
| sansesanjiegao | INT | NOT NULL DEFAULT 0 | 三色三节高次数 |
| wufanhe | INT | NOT NULL DEFAULT 0 | 无番和次数 |
| miaoshouhuichun | INT | NOT NULL DEFAULT 0 | 妙手回春次数 |
| haidilaoyue | INT | NOT NULL DEFAULT 0 | 海底捞月次数 |
| gangshangkaihua | INT | NOT NULL DEFAULT 0 | 杠上开花次数 |
| qiangganghe | INT | NOT NULL DEFAULT 0 | 抢杠和次数 |
| pengpenghe | INT | NOT NULL DEFAULT 0 | 碰碰和次数 |
| hunyise | INT | NOT NULL DEFAULT 0 | 混一色次数 |
| sansesanbugao | INT | NOT NULL DEFAULT 0 | 三色三步高次数 |
| wumenqi | INT | NOT NULL DEFAULT 0 | 五门齐次数 |
| quanqiuren | INT | NOT NULL DEFAULT 0 | 全求人次数 |
| shuangangang | INT | NOT NULL DEFAULT 0 | 双暗杠次数 |
| shuangjianke | INT | NOT NULL DEFAULT 0 | 双箭刻次数 |
| quandaiyao | INT | NOT NULL DEFAULT 0 | 全带幺次数 |
| buqiuren | INT | NOT NULL DEFAULT 0 | 不求人次数 |
| shuangminggang | INT | NOT NULL DEFAULT 0 | 双明杠次数 |
| hejuezhang | INT | NOT NULL DEFAULT 0 | 和绝张次数 |
| jianke | INT | NOT NULL DEFAULT 0 | 箭刻次数 |
| quanfengke | INT | NOT NULL DEFAULT 0 | 圈风刻次数 |
| menfengke | INT | NOT NULL DEFAULT 0 | 门风刻次数 |
| menqianqing | INT | NOT NULL DEFAULT 0 | 门前清次数 |
| pinghe | INT | NOT NULL DEFAULT 0 | 平和次数 |
| siguiyi | INT | NOT NULL DEFAULT 0 | 四归一次数 |
| shuangtongke | INT | NOT NULL DEFAULT 0 | 双同刻次数 |
| shuanganke | INT | NOT NULL DEFAULT 0 | 双暗刻次数 |
| angang | INT | NOT NULL DEFAULT 0 | 暗杠次数 |
| duanyao | INT | NOT NULL DEFAULT 0 | 断幺次数 |
| yibangao | INT | NOT NULL DEFAULT 0 | 一般高次数 |
| xixiangfeng | INT | NOT NULL DEFAULT 0 | 喜相逢次数 |
| lianliu | INT | NOT NULL DEFAULT 0 | 连六次数 |
| laoshaofu | INT | NOT NULL DEFAULT 0 | 老少副次数 |
| yaojiuke | INT | NOT NULL DEFAULT 0 | 幺九刻次数 |
| minggang | INT | NOT NULL DEFAULT 0 | 明杠次数 |
| queyimen | INT | NOT NULL DEFAULT 0 | 缺一门次数 |
| wuzi | INT | NOT NULL DEFAULT 0 | 无字次数 |
| bianzhang | INT | NOT NULL DEFAULT 0 | 边张次数 |
| qianzhang | INT | NOT NULL DEFAULT 0 | 嵌张次数 |
| dandiaojiang | INT | NOT NULL DEFAULT 0 | 单钓将次数 |
| zimo | INT | NOT NULL DEFAULT 0 | 自摸次数 |
| huapai | INT | NOT NULL DEFAULT 0 | 花牌次数 |
| mingangang | INT | NOT NULL DEFAULT 0 | 明暗杠次数 |


