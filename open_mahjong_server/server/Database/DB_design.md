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



### record_stats 对局统计宽表

统一使用一张宽表记录所有规则的统计数据，按照 `rule`（规则，如 GB/JP）与 `mode`（模式，如 `1/4`、`2/4_rank` 等）区分不同维度。客户端展示排行榜/统计时只需按条件查询该表。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | NOT NULL REFERENCES users(user_id) ON DELETE CASCADE | 用户 ID |
| rule | VARCHAR(10) | NOT NULL | 规则标识（例如：GB、JP） |
| mode | VARCHAR(20) | NOT NULL | 数据模式（`1/4`、`2/4`、`3/4`、`4/4`、`1/4_rank` 等） |
| total_games | INT | NOT NULL DEFAULT 0 | 总对局数 |
| total_rounds | INT | NOT NULL DEFAULT 0 | 累计回合数 |
| win_count | INT | NOT NULL DEFAULT 0 | 和牌次数 |
| self_draw_count | INT | NOT NULL DEFAULT 0 | 自摸次数 |
| deal_in_count | INT | NOT NULL DEFAULT 0 | 放铳次数 |
| total_fan_score | INT | NOT NULL DEFAULT 0 | 累计番数 |
| total_win_turn | INT | NOT NULL DEFAULT 0 | 累计和巡（用于计算平均巡目） |
| total_fangchong_score | INT | NOT NULL DEFAULT 0 | 累计放铳分 |
| first_place_count | INT | NOT NULL DEFAULT 0 | 一位次数 |
| second_place_count | INT | NOT NULL DEFAULT 0 | 二位次数 |
| third_place_count | INT | NOT NULL DEFAULT 0 | 三位次数 |
| fourth_place_count | INT | NOT NULL DEFAULT 0 | 四位次数 |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 最近更新时间 |

> 主键：`(user_id, rule, mode)`，支持一个玩家在不同规则/模式下分别累计数据。

#### 番种统计（与主表合并存储）
以下字段全部位于 `record_stats` 中，每列都以 `INT NOT NULL DEFAULT 0` 记录对应番种累计出现次数。示例：

| 字段名 | 说明 |
|--------|------|
| dasixi | 大四喜次数 |
| dasanyuan | 大三元次数 |
| lvyise | 绿一色次数 |
| jiulianbaodeng | 九莲宝灯次数 |
| sigang | 四杠次数 |
| sangang | 三杠次数 |
| lianqidui | 连七对次数 |
| shisanyao | 十三幺次数 |
| qingyaojiu | 清幺九次数 |
| xiaosixi | 小四喜次数 |
| xiaosanyuan | 小三元次数 |
| ziyise | 字一色次数 |
| sianke | 四暗刻次数 |
| yiseshuanglonghui | 一色双龙会次数 |
| yisesitongshun | 一色四同顺次数 |
| yisesijiegao | 一色四节高次数 |
| yisesibugao | 一色四步高次数 |
| hunyaojiu | 混幺九次数 |
| qiduizi | 七对子次数 |
| qixingbukao | 七星不靠次数 |
| quanshuangke | 全双刻次数 |
| qingyise | 清一色次数 |
| yisesantongshun | 一色三同顺次数 |
| yisesanjiegao | 一色三节高次数 |
| quanda | 全大次数 |
| quanzhong | 全中次数 |
| quanxiao | 全小次数 |
| qinglong | 清龙次数 |
| sanseshuanglonghui | 三色双龙会次数 |
| yisesanbugao | 一色三步高次数 |
| quandaiwu | 全带五次数 |
| santongke | 三同刻次数 |
| sananke | 三暗刻次数 |
| quanbukao | 全不靠次数 |
| zuhelong | 组合龙次数 |
| dayuwu | 大于五次数 |
| xiaoyuwu | 小于五次数 |
| sanfengke | 三风刻次数 |
| hualong | 花龙次数 |
| tuibudao | 推不倒次数 |
| sansesantongshun | 三色三同顺次数 |
| sansesanjiegao | 三色三节高次数 |
| wufanhe | 无番和次数 |
| miaoshouhuichun | 妙手回春次数 |
| haidilaoyue | 海底捞月次数 |
| gangshangkaihua | 杠上开花次数 |
| qiangganghe | 抢杠和次数 |
| pengpenghe | 碰碰和次数 |
| hunyise | 混一色次数 |
| sansesanbugao | 三色三步高次数 |
| wumenqi | 五门齐次数 |
| quanqiuren | 全求人次数 |
| shuangangang | 双暗杠次数 |
| shuangjianke | 双箭刻次数 |
| quandaiyao | 全带幺次数 |
| buqiuren | 不求人次数 |
| shuangminggang | 双明杠次数 |
| hejuezhang | 和绝张次数 |
| jianke | 箭刻次数 |
| quanfengke | 圈风刻次数 |
| menfengke | 门风刻次数 |
| menqianqing | 门前清次数 |
| pinghe | 平和次数 |
| siguiyi | 四归一次数 |
| shuangtongke | 双同刻次数 |
| shuanganke | 双暗刻次数 |
| angang | 暗杠次数 |
| duanyao | 断幺次数 |
| yibangao | 一般高次数 |
| xixiangfeng | 喜相逢次数 |
| lianliu | 连六次数 |
| laoshaofu | 老少副次数 |
| yaojiuke | 幺九刻次数 |
| minggang | 明杠次数 |
| queyimen | 缺一门次数 |
| wuzi | 无字次数 |
| bianzhang | 边张次数 |
| qianzhang | 嵌张次数 |
| dandiaojiang | 单钓将次数 |
| zimo | 自摸次数 |
| huapai | 花牌次数 |
| mingangang | 明暗杠次数 |


