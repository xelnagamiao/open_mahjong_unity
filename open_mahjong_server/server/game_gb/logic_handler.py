# 存储逻辑处理

# 用于传递自身索引:对方索引 获取自己与对方的相对位置
def get_index_relative_position(self,self_index,other_index):
    if self_index == 0:
        if other_index == 1:
            return "right"
        elif other_index == 2:
            return "top"
        elif other_index == 3:
            return "left"
        elif other_index == 0:
            return "self"
    elif self_index == 1:
        if other_index == 0:
            return "left"
        elif other_index == 2:
            return "right"
        elif other_index == 3:
            return "top"
        elif other_index == 1:
            return "self"
    elif self_index == 2:
        if other_index == 0:
            return "top"
        elif other_index == 1:
            return "left"
        elif other_index == 3:
            return "right"
        elif other_index == 2:
            return "self"
    elif self_index == 3:
        if other_index == 0:
            return "right"
        elif other_index == 1:
            return "top"
        elif other_index == 2:
            return "left"
        elif other_index == 3:
            return "self"