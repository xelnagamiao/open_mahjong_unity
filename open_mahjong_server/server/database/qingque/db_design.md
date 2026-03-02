## 青雀麻将统计表设计

### qingque_history_stats 基础统计数据表

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | NOT NULL, REFERENCES users(user_id) ON DELETE CASCADE | 用户ID |
| rule | VARCHAR(10) | NOT NULL | 规则类型（qingque） |
| mode | VARCHAR(20) | NOT NULL | 游戏模式（如 "4/4"） |
| total_games | INT | DEFAULT 0 | 总对局数 |
| total_rounds | INT | DEFAULT 0 | 总局数 |
| win_count | INT | DEFAULT 0 | 和牌次数 |
| self_draw_count | INT | DEFAULT 0 | 自摸次数 |
| deal_in_count | INT | DEFAULT 0 | 放铳次数 |
| total_fan_score | INT | DEFAULT 0 | 总和牌番数 |
| total_win_turn | INT | DEFAULT 0 | 总和牌巡目 |
| total_fangchong_score | INT | DEFAULT 0 | 总放铳番数 |
| first_place_count | INT | DEFAULT 0 | 一位次数 |
| second_place_count | INT | DEFAULT 0 | 二位次数 |
| third_place_count | INT | DEFAULT 0 | 三位次数 |
| fourth_place_count | INT | DEFAULT 0 | 四位次数 |
| PRIMARY KEY | (user_id, rule, mode) | 复合主键 | |

### qingque_fan_stats 番种统计数据表

基于 FanTextDictionary.cs 的 FanToDisplayQingque 番表，记录每个番种的出现次数。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | NOT NULL, REFERENCES users(user_id) ON DELETE CASCADE | 用户ID |
| rule | VARCHAR(10) | NOT NULL | 规则类型（qingque） |
| mode | VARCHAR(20) | NOT NULL | 游戏模式 |
| hepai | INT | DEFAULT 0 | 和牌 |
| tianhe | INT | DEFAULT 0 | 天和 |
| dihe | INT | DEFAULT 0 | 地和 |
| lingshangkaihua | INT | DEFAULT 0 | 岭上开花 |
| haidilaoyue | INT | DEFAULT 0 | 海底捞月 |
| hedilaoyue | INT | DEFAULT 0 | 河底捞鱼 |
| qianggang | INT | DEFAULT 0 | 抢杠 |
| qidui | INT | DEFAULT 0 | 七对 |
| menqianqing | INT | DEFAULT 0 | 门前清 |
| siangang | INT | DEFAULT 0 | 四暗杠 |
| sanangang | INT | DEFAULT 0 | 三暗杠 |
| shuangangang | INT | DEFAULT 0 | 双暗杠 |
| angang | INT | DEFAULT 0 | 暗杠 |
| sigang | INT | DEFAULT 0 | 四杠 |
| sangang | INT | DEFAULT 0 | 三杠 |
| shuanggang | INT | DEFAULT 0 | 双杠 |
| sianke | INT | DEFAULT 0 | 四暗刻 |
| sananke | INT | DEFAULT 0 | 三暗刻 |
| duiduihe | INT | DEFAULT 0 | 对对和 |
| shiergui | INT | DEFAULT 0 | 十二归 |
| bagui | INT | DEFAULT 0 | 八归 |
| sandiedui | INT | DEFAULT 0 | 三叠对 |
| erdiedui | INT | DEFAULT 0 | 二叠对 |
| diedui | INT | DEFAULT 0 | 叠对 |
| ziyise | INT | DEFAULT 0 | 字一色 |
| dasixi | INT | DEFAULT 0 | 大四喜 |
| xiaosixi | INT | DEFAULT 0 | 小四喜 |
| sixidui | INT | DEFAULT 0 | 四喜对 |
| fengpaisanke | INT | DEFAULT 0 | 风牌三刻 |
| fengpaiqidui | INT | DEFAULT 0 | 风牌七对 |
| fengpailiudui | INT | DEFAULT 0 | 风牌六对 |
| fengpaiwudui | INT | DEFAULT 0 | 风牌五对 |
| fengpaisidui | INT | DEFAULT 0 | 风牌四对 |
| dasanyuan | INT | DEFAULT 0 | 大三元 |
| xiaosanyuan | INT | DEFAULT 0 | 小三元 |
| sanyuanliudui | INT | DEFAULT 0 | 三元六对 |
| sanyuandui | INT | DEFAULT 0 | 三元对 |
| fanpaisike | INT | DEFAULT 0 | 番牌四刻 |
| fanpaisanke | INT | DEFAULT 0 | 番牌三刻 |
| fanpaierke | INT | DEFAULT 0 | 番牌二刻 |
| fanpaike | INT | DEFAULT 0 | 番牌刻 |
| fanpaiqidui | INT | DEFAULT 0 | 番牌七对 |
| fanpailiudui | INT | DEFAULT 0 | 番牌六对 |
| fanpaiwudui | INT | DEFAULT 0 | 番牌五对 |
| fanpaisifu | INT | DEFAULT 0 | 番牌四副 |
| fanpaisanfu | INT | DEFAULT 0 | 番牌三副 |
| fanpaierfu | INT | DEFAULT 0 | 番牌二副 |
| fanpai | INT | DEFAULT 0 | 番牌 |
| qingyaojiu | INT | DEFAULT 0 | 清幺九 |
| hunyaojiu | INT | DEFAULT 0 | 混幺九 |
| qingdaiyao | INT | DEFAULT 0 | 清带幺 |
| hundaiyao | INT | DEFAULT 0 | 混带幺 |
| jiulianbaodeng | INT | DEFAULT 0 | 九莲宝灯 |
| qingyise | INT | DEFAULT 0 | 清一色 |
| hunyise | INT | DEFAULT 0 | 混一色 |
| wumenqi | INT | DEFAULT 0 | 五门齐 |
| hunyishu | INT | DEFAULT 0 | 混一数 |
| ershu | INT | DEFAULT 0 | 二数 |
| erju | INT | DEFAULT 0 | 二聚 |
| sanju | INT | DEFAULT 0 | 三聚 |
| siju | INT | DEFAULT 0 | 四聚 |
| lianshu | INT | DEFAULT 0 | 连数 |
| jianshu | INT | DEFAULT 0 | 间数 |
| jingshu | INT | DEFAULT 0 | 镜数 |
| yingshu | INT | DEFAULT 0 | 映数 |
| mantingfang | INT | DEFAULT 0 | 满庭芳 |
| sitongshun | INT | DEFAULT 0 | 四同顺 |
| santongshun | INT | DEFAULT 0 | 三同顺 |
| erbangao | INT | DEFAULT 0 | 二般高 |
| yibangao | INT | DEFAULT 0 | 一般高 |
| silianke | INT | DEFAULT 0 | 四连刻 |
| sanlianke | INT | DEFAULT 0 | 三连刻 |
| sibugao | INT | DEFAULT 0 | 四步高 |
| sanbugao | INT | DEFAULT 0 | 三步高 |
| silianhuan | INT | DEFAULT 0 | 四连环 |
| sanlianhuan | INT | DEFAULT 0 | 三连环 |
| yiqiguantong | INT | DEFAULT 0 | 一气贯通 |
| qiliandui | INT | DEFAULT 0 | 七连对 |
| liuliandui | INT | DEFAULT 0 | 六连对 |
| wuliandui | INT | DEFAULT 0 | 五连对 |
| siliandui | INT | DEFAULT 0 | 四连对 |
| sansetongke | INT | DEFAULT 0 | 三色同刻 |
| sansetongshun | INT | DEFAULT 0 | 三色同顺 |
| sanseedui | INT | DEFAULT 0 | 三色二对 |
| sansetongdui | INT | DEFAULT 0 | 三色同对 |
| sanselianke | INT | DEFAULT 0 | 三色连刻 |
| sanseguantong | INT | DEFAULT 0 | 三色贯通 |
| jingtong | INT | DEFAULT 0 | 镜同 |
| jingtongsandui | INT | DEFAULT 0 | 镜同三对 |
| jingtongerdui | INT | DEFAULT 0 | 镜同二对 |
| shuanglonghui | INT | DEFAULT 0 | 双龙会 |
| PRIMARY KEY | (user_id, rule, mode) | 复合主键 | |
