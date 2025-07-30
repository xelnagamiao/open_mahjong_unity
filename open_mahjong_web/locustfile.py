# 网页承载测试文件
from locust import HttpUser, task, between

class MahjongUser(HttpUser):
    wait_time = between(1, 5)  # 模拟用户等待时间在1到5秒之间

    @task
    def index(self):
        self.client.get("/")  # 测试首页

    @task
    def count(self):
        data = {
            "hand": "123s456p789m东东东白白",
            "fulu1": "",
            "fulu2": "",
            "fulu3": "",
            "fulu4": "",
            "wayToHepai": "wayToHepaiZi",  # 自摸
            "doraNum": "2",
            "deepDoraNum": "1",
            "positionSelect": "positionDong",  # 东(自亲)
            "publicPositionSelect": "publicPositionDong"  # 场风东
        }
        self.client.post("/count", data=data)  # 测试 /count 路由