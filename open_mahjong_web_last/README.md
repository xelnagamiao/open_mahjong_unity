本项目用简单python web框架Flask开发，用以解出麻将手牌的和牌番役。

# 遇到报错请复制黏贴至gpt，中间可能遇到openssl版本不足，apt包管理器太旧，pip未更新，python3-devel开发库未安装等问题。

# 启动一台debian12服务器
1.安装python 3.10.0及以上版本，一般是自带的
# git clone项目
2.git clone https://gitee.com/q1448826180/mahjong_web.git
# cd项目根目录 --> mahong_web文件夹内

# 安装virtualenv
2.pip install virtualenv
# 创建虚拟环境
3.virtualenv mahjong_web_venv
# 激活虚拟环境
4.source mahjong_web_venv/bin/activate
# 安装依赖包————————————————————————————————————————————————————————虚拟环境下操作
5.pip3 install -r requirements.txt
# 安装WSGI 服务器guniorn
pip3 install gunicorn
# 使用Nginx重定向外部请求至http://127.0.0.1:8000;

# 使用debian12下载supervisor
sudo apt install supervisor
# 配置supervisor
sudo nano /etc/supervisor/conf.d/mahjong_web.conf
# 内容如下： 第一行用于使用gunicorn启动wsgi服务,第二行指定wsgi服务启动的目录
[program:mahjong_web]
command=/www/wwwroot/mahjong.fit/mahjong_web/mahjong_web_venv/bin/gunicorn -w 4 -b 127.0.0.1:8000 app:app
directory=/www/wwwroot/mahjong.fit/mahjong_web
user=root
autostart=true
autorestart=true # 自动重启
stderr_logfile=/var/log/mahjong_web.err.log
stdout_logfile=/var/log/mahjong_web.out.log
# 更新supervisor状态
sudo supervisorctl reread
sudo supervisorctl update
# 启动supervisor
sudo supervisorctl start mahjong_web
# 查看状态
sudo supervisorctl status

# ——————————————————————————————————————————————————————————————退出虚拟环境

# 停止项目
sudo supervisorctl stop mahjong_web
# 启动项目
sudo supervisorctl start mahjong_web
