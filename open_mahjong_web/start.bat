@echo off
echo 启动 salasasa.cn 麻将分析网站...

echo 检查 Node.js 环境...
node --version >nul 2>&1
if errorlevel 1 (
    echo 错误: 未找到 Node.js，请先安装 Node.js
    pause
    exit /b 1
)

echo 安装所有依赖...
echo 安装后端依赖...
npm install

echo 安装前端依赖...
cd client
npm install
cd ..

echo.
echo 所有依赖安装完成！
echo.
echo 启动开发服务器...
echo 前端地址: http://localhost:5173
echo 后端API: http://localhost:3000/api
echo.
npm run dev

pause 