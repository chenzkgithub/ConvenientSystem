# Python 知识库

> 涵盖 Python 从入门到精通的核心知识体系，包含语法基础、进阶特性、主流框架与实战教程。
> 每个知识点都配有详细讲解、代码示例、常见陷阱和最佳实践。

---

## 目录

- [一、Python 基础](#一python-基础)
- [二、数据结构](#二数据结构)
- [三、函数与模块](#三函数与模块)
- [四、面向对象编程](#四面向对象编程)
- [五、文件与 IO](#五文件与-io)
- [六、异常处理](#六异常处理)
- [七、进阶特性](#七进阶特性)
- [八、并发编程](#八并发编程)
- [九、标准库精选](#九标准库精选)
- [十、Web 开发](#十web-开发)
- [十一、数据科学与 AI](#十一数据科学与-ai)
- [十二、数据库操作](#十二数据库操作)
- [十三、测试与调试](#十三测试与调试)
- [十四、工程化实践](#十四工程化实践)
- [十五、学习资源与路线图](#十五学习资源与路线图)

---

## 一、Python 基础

### 1.1 环境搭建

Python 的环境管理是新手最容易踩坑的地方。核心原则：**每个项目使用独立的虚拟环境**，避免不同项目的依赖版本互相冲突。

**安装 Python**

推荐从官网下载最新稳定版（3.10+）。Windows 用户安装时务必勾选 **"Add Python to PATH"**，否则命令行无法直接使用 `python` 命令。

```bash
# 验证安装
python --version       # Python 3.12.x

# Windows 可能有两个版本共存，用 py 启动器指定
py -3.10 --version
py -3.12 --version
```

**虚拟环境（必须掌握）**

虚拟环境会为每个项目创建独立的 Python 解释器和包目录。这样项目 A 用 `requests==2.28`，项目 B 用 `requests==2.31`，互不干扰。

```bash
# 创建虚拟环境（在项目根目录下）
python -m venv .venv

# 激活虚拟环境
.venv\Scripts\activate       # Windows (PowerShell)
# source .venv/bin/activate  # Linux/Mac

# 激活后命令行提示符前面会出现 (.venv)
# 此时 pip install 的包都装在这个虚拟环境里

# 退出虚拟环境
deactivate
```

> **常见陷阱**：VS Code 打开项目后需要选择 Python 解释器（Ctrl+Shift+P → Python: Select Interpreter → 选择 `.venv` 中的那个），否则编辑器内的代码提示和终端运行的可能不是同一个环境。

**pip 包管理**

```bash
pip install requests                  # 安装最新稳定版
pip install requests==2.31.0          # 安装指定版本
pip install "requests>=2.28,<3.0"     # 版本范围
pip install -r requirements.txt       # 从文件批量安装
pip install --upgrade requests        # 升级

pip freeze > requirements.txt         # 导出当前环境所有依赖及版本
pip list                              # 查看已安装的包
pip show requests                     # 查看某个包的详细信息
pip uninstall requests                # 卸载
```

> **最佳实践**：`requirements.txt` 里锁定版本号（`requests==2.31.0`），保证团队和部署环境一致。更现代的方案用 `pyproject.toml` + `pip-tools` 或 `Poetry`。

**pyenv（多版本管理，推荐）**

当项目需要不同版本的 Python 时，pyenv 可以方便地切换：

```bash
# 安装 pyenv（Windows 用 pyenv-win）
# https://github.com/pyenv-win/pyenv-win

pyenv install 3.10.13     # 安装指定版本
pyenv install 3.12.1
pyenv versions             # 列出已安装版本
pyenv global 3.12.1        # 设置全局默认
pyenv local 3.10.13        # 在当前目录设置版本（会创建 .python-version 文件）
```

### 1.2 变量与数据类型

Python 是**动态强类型**语言：变量不需要声明类型（动态），但不会隐式做不安全的类型转换（强类型）。`"1" + 2` 会报错，不会自动把字符串转成数字。

**基本数据类型**

```python
# 整数 int —— Python 3 的 int 没有大小限制，支持任意精度
a = 42
big = 10 ** 100           # 大整数不会溢出
print(type(big))           # <class 'int'>

# 浮点数 float —— 基于 C 的 double（64 位），有精度问题
0.1 + 0.2                # 0.30000000000000004（不是 0.3！）
# 需要精确计算时用 decimal 模块
from decimal import Decimal
Decimal("0.1") + Decimal("0.2")  # Decimal('0.3')

# 布尔值 bool —— 是 int 的子类！
True + True              # 2
isinstance(True, int)    # True

# None —— 表示"没有值"，单例对象
x = None
x is None                # 用 is 判断，不用 ==
```

> **浮点精度陷阱**：涉及金额计算时，永远不要用 float。用 `Decimal`（精确十进制）或存整数分（`100` 表示 1 元）。

**字符串 str**

字符串是**不可变序列**，任何修改操作都会创建新字符串。

```python
# 创建
s1 = "hello"
s2 = 'hello'              # 单双引号等价
s3 = """多行
字符串"""                   # 三引号保留换行
s4 = r"C:\new\test"       # raw 字符串，不转义（正则和路径常用）

# 常用方法
"hello world".upper()              # "HELLO WORLD"
"  hello  ".strip()                # "hello"（去两端空白）
"hello".startswith("he")           # True
"hello".find("ll")                 # 2（找不到返回 -1）
"hello".replace("l", "L")          # "heLLo"
"a,b,c".split(",")                 # ["a", "b", "c"]
"-".join(["a", "b", "c"])          # "a-b-c"
"hello".center(20, "-")            # "-------hello--------"

# 字符串切片（和 list 一样支持切片语法）
s = "Hello, World!"
s[0:5]      # "Hello"
s[-1]       # "!"
s[::-1]     # "!dlroW ,olleH"（反转）

# 判断类方法
"123".isdigit()       # True
"abc".isalpha()       # True
"abc123".isalnum()    # True
```

**f-string 格式化（Python 3.6+，强烈推荐）**

```python
name = "Alice"
age = 25
price = 99.5

# 基础用法
print(f"姓名：{name}，年龄：{age}")

# 格式控制
print(f"价格：{price:.2f}")        # 99.50（保留 2 位小数）
print(f"百分比：{0.856:.1%}")      # 85.6%
print(f"补零：{42:06d}")           # 000042
print(f"左对齐：{'hi':<10}|")      # "hi        |"
print(f"右对齐：{'hi':>10}|")      # "        hi|"
print(f"居中：{'hi':^10}|")        # "    hi    |"
print(f"千分位：{1234567:,}")       # 1,234,567

# 可以放表达式
print(f"{2 + 3 = }")               # "2 + 3 = 5"（Python 3.8+ 调试利器）
print(f"{name.upper() = }")        # "name.upper() = 'ALICE'"

# 多行 f-string
msg = f"""
尊敬的 {name}：
  您的账户余额为 {price:.2f} 元。
"""
```

**类型转换与检查**

```python
# 类型转换
int("42")          # 42
float("3.14")      # 3.14
str(42)            # "42"
bool(0)            # False（0、空字符串、None、空容器都是 False）
bool("hello")      # True
list("abc")        # ['a', 'b', 'c']
tuple([1, 2, 3])   # (1, 2, 3)

# 类型检查
type(42) == int              # True
isinstance(42, int)          # True（推荐，支持继承关系）
isinstance(True, int)        # True（bool 是 int 的子类）
type(42) is int              # True（严格比较，不考虑继承）
```

> **isinstance vs type**：优先用 `isinstance`，它考虑继承关系。`type(True) is int` 返回 `False`，但 `isinstance(True, int)` 返回 `True`。

### 1.3 运算符

```python
# ===== 算术运算 =====
10 / 3       # 3.333...  真除法（结果永远是 float）
10 // 3      # 3         整除（向下取整，负数注意：-10 // 3 = -4）
10 % 3       # 1         取余（符号跟除数：-10 % 3 = 2）
2 ** 10      # 1024      幂运算

# ===== 比较运算 =====
x == y       # 值相等（会调用 __eq__）
x != y       # 值不等
x is y       # 同一对象（id 相同，内存地址一样）
x is not y   # 不同对象

# == vs is 的区别（面试高频题）
a = [1, 2, 3]
b = [1, 2, 3]
a == b       # True（值相等）
a is b       # False（不是同一个对象）

# Python 对小整数（-5 ~ 256）和短字符串有缓存
x = 256
y = 256
x is y       # True（缓存）

x = 257
y = 257
x is y       # False（超出缓存范围）

# ===== 逻辑运算（短路求值） =====
True and False   # False（遇到第一个 False 就停止）
True or False    # True（遇到第一个 True 就停止）
not True         # False

# 短路求值的实际应用
# 避免 NoneType 错误
user and user.name        # user 为 None 时不会报错

# 给变量赋默认值
name = input_name or "默认名称"   # input_name 为空/None 时用默认值

# ===== 成员运算 =====
"hello" in "hello world"    # True
3 in [1, 2, 3, 4]          # True
"key" in {"key": "value"}   # True（检查键是否存在）

# ===== 海象运算符（Python 3.8+） =====
# 在表达式中同时赋值，减少重复计算
if (n := len(data)) > 10:
    print(f"数据量过大：{n} 条")

# 在 while 循环中很实用
while (line := file.readline()).strip():
    process(line)
```

### 1.4 控制流

```python
# ===== if-elif-else =====
score = 85
if score >= 90:
    grade = "A"
elif score >= 80:
    grade = "B"
elif score >= 70:
    grade = "C"
else:
    grade = "D"

# 三元表达式（条件写在中间）
status = "成年" if age >= 18 else "未成年"
# 等价于：
# if age >= 18:
#     status = "成年"
# else:
#     status = "未成年"

# ===== for 循环 =====
# range 的三种用法
for i in range(5):          # 0, 1, 2, 3, 4
    print(i)

for i in range(2, 8):       # 2, 3, 4, 5, 6, 7
    print(i)

for i in range(0, 10, 2):   # 0, 2, 4, 6, 8（步长 2）
    print(i)

# 带索引遍历（比 range(len()) 更 Pythonic）
fruits = ["apple", "banana", "cherry"]
for i, fruit in enumerate(fruits):
    print(f"{i}: {fruit}")

# 同时遍历多个序列
names = ["Alice", "Bob"]
ages = [25, 30]
for name, age in zip(names, ages):
    print(f"{name} is {age}")

# for-else（循环正常结束时执行 else，break 跳出则不执行）
for n in range(2, 10):
    for x in range(2, n):
        if n % x == 0:
            break
    else:
        # 循环没有被 break 中断时执行
        print(f"{n} 是质数")

# ===== while 循环 =====
count = 0
while count < 5:
    print(count)
    count += 1

# while-else（和 for-else 类似）
while condition:
    do_something()
else:
    # condition 变为 False 时执行（不是 break 退出）
    print("循环正常结束")

# ===== break / continue / pass =====
for i in range(10):
    if i == 3:
        continue    # 跳过本次，进入下一次
    if i == 7:
        break       # 直接跳出循环
    print(i)        # 输出 0 1 2 4 5 6

# pass 是空操作，用作占位符
class MyClass:
    pass            # 暂时不实现，先占位

if condition:
    pass            # 稍后补充逻辑

# ===== match-case（Python 3.10+ 结构化模式匹配） =====
# 比 if-elif 链更清晰，支持解构
match status_code:
    case 200:
        print("成功")
    case 404:
        print("未找到")
    case 500 | 502 | 503:     # 多个值匹配
        print("服务器错误")
    case code if code >= 400:  # 带守卫条件
        print(f"客户端错误：{code}")
    case _:                    # 默认分支（类似 default）
        print("未知状态")

# 模式匹配支持解构
match command.split():
    case ["quit"]:
        print("退出")
    case ["go", direction]:
        print(f"向 {direction} 走")
    case ["go", direction, distance]:
        print(f"向 {direction} 走 {distance} 步")
```

> **for-else 的语义**：else 块在循环**没有被 break 中断**时执行。常被误解为"循环失败时执行"，实际是"循环正常完成时执行"。在搜索场景中非常有用：找到了就 break，没找到就走 else。

---

## 二、数据结构

Python 内置四种数据结构：**列表（list）、元组（tuple）、字典（dict）、集合（set）**。它们各有特点，适用于不同场景。

| 类型 | 有序 | 可变 | 重复 | 语法 | 用途 |
|------|------|------|------|------|------|
| list | ✅ | ✅ | ✅ | `[1,2,3]` | 通用有序集合 |
| tuple | ✅ | ❌ | ✅ | `(1,2,3)` | 不可变记录、字典键 |
| dict | ✅* | ✅ | 键唯一 | `{"k":"v"}` | 键值映射 |
| set | ❌ | ✅ | ❌ | `{1,2,3}` | 去重、集合运算 |

> *Python 3.7+ dict 保持插入顺序

### 2.1 列表 list

列表是 Python 中最常用的数据结构，底层是**动态数组**（不是链表）。随机访问 O(1)，末尾增删 O(1) 均摊，中间插入删除 O(n)。

```python
# ===== 创建 =====
nums = [1, 2, 3, 4, 5]
empty = []
from_range = list(range(10))         # [0, 1, 2, ..., 9]
from_string = list("hello")          # ['h', 'e', 'l', 'l', 'o']
copy = nums.copy()                   # 浅拷贝
copy2 = nums[:]                      # 切片也是浅拷贝

# ===== 访问与切片 =====
nums[0]        # 1（第一个）
nums[-1]       # 5（最后一个）
nums[1:3]      # [2, 3]（左闭右开）
nums[::2]      # [1, 3, 5]（步长 2）
nums[::-1]     # [5, 4, 3, 2, 1]（反转）

# ===== 增删改 =====
nums.append(6)          # 末尾添加 → [1,2,3,4,5,6]
nums.extend([7, 8])     # 末尾扩展多个 → [1,2,3,4,5,6,7,8]
nums.insert(0, 0)       # 指定位置插入 → [0,1,2,3,4,5,6,7,8]

nums.remove(3)          # 删除第一个值为 3 的元素（不存在会报错）
nums.pop()              # 弹出并返回末尾元素
nums.pop(0)             # 弹出并返回指定位置元素
del nums[0]             # 删除指定位置（不返回值）
del nums[1:3]           # 删除切片

nums.clear()            # 清空列表 → []

# ===== 查找 =====
3 in nums               # True（O(n) 线性查找）
nums.index(3)           # 返回第一个 3 的索引（不存在会报错）
nums.count(3)           # 统计 3 出现的次数

# ===== 排序 =====
nums = [3, 1, 4, 1, 5, 9, 2, 6]

nums.sort()                          # 原地升序 → [1,1,2,3,4,5,6,9]
nums.sort(reverse=True)              # 原地降序
sorted_nums = sorted(nums)           # 返回新列表，不修改原列表
sorted_nums = sorted(nums, reverse=True)

# 自定义排序
words = ["banana", "apple", "cherry", "date"]
sorted(words, key=len)               # 按长度排序 → ['date', 'apple', 'banana', 'cherry']
sorted(words, key=str.lower)         # 忽略大小写

# 按字典的某个键排序
users = [{"name": "Bob", "age": 30}, {"name": "Alice", "age": 25}]
sorted(users, key=lambda u: u["age"])         # 按年龄升序
sorted(users, key=lambda u: u["age"], reverse=True)  # 按年龄降序

# ===== 列表推导式（Python 最强大的特性之一） =====
# 基本形式
squares = [x**2 for x in range(10)]
# 等价于：
# squares = []
# for x in range(10):
#     squares.append(x**2)

# 带条件过滤
evens = [x for x in range(20) if x % 2 == 0]

# 嵌套（展平二维列表）
matrix = [[1, 2, 3], [4, 5, 6], [7, 8, 9]]
flat = [x for row in matrix for x in row]   # [1,2,3,4,5,6,7,8,9]

# 字典列表提取某个字段
names = [u["name"] for u in users]

# 条件表达式在推导式中
labels = ["偶" if x % 2 == 0 else "奇" for x in range(10)]
```

> **性能提示**：频繁在列表头部插入/删除（`insert(0, x)` / `pop(0)`）效率很低，因为所有元素都要移动。如果需要频繁的头部操作，用 `collections.deque`（双端队列，两端操作都是 O(1)）。

> **浅拷贝陷阱**：`list.copy()` 和 `list[:]` 都是浅拷贝——外层是新列表，但内部嵌套对象仍是引用。需要完全独立的副本用 `copy.deepcopy()`。
> ```python
> original = [[1, 2], [3, 4]]
> shallow = original.copy()
> shallow[0].append(99)   # original[0] 也变成了 [1, 2, 99]！
> ```

### 2.2 元组 tuple

元组是**不可变列表**。一旦创建就不能修改（增删改都不行）。

**为什么要用元组？**

1. **安全性**：数据不应被修改时用 tuple，防止意外篡改
2. **可哈希**：tuple 可以作为字典的键或 set 的元素（list 不行）
3. **性能**：创建速度和内存都优于 list（Python 内部还会缓存小 tuple）
4. **语义**：tuple 表示"结构/记录"（ heterogeneous ），list 表示"集合"（homogeneous）

```python
# 创建
point = (3, 4)
single = (42,)         # 单元素 tuple 必须加逗号！
empty = ()
no_parens = 1, 2, 3    # 不加括号也行（但不推荐）

# 不可变
point[0] = 10          # TypeError!

# 解包（unpacking）—— 非常常用
x, y = point
first, *rest = (1, 2, 3, 4, 5)    # first=1, rest=[2,3,4,5]
first, *middle, last = (1, 2, 3, 4, 5)  # middle=[2,3,4]

# 交换变量（Python 特有的优雅写法）
a, b = 1, 2
a, b = b, a            # 不需要临时变量

# 函数返回多个值（实际返回的是 tuple）
def divide(a, b):
    return a // b, a % b    # 返回 (商, 余)

quotient, remainder = divide(17, 5)   # 3, 2

# 命名元组 —— 给字段起名，增强可读性
from collections import namedtuple

Point = namedtuple("Point", ["x", "y"])
p = Point(3, 4)
p.x          # 3
p.y          # 4
p[0]         # 3（仍然支持下标访问）

# 比普通 tuple 可读性强得多
# 对比：
position = (3, 4)           # 不知道 3 和 4 分别是什么
position = Point(3, 4)      # 明确是 x=3, y=4
```

### 2.3 字典 dict

字典是 Python 的灵魂数据结构，底层是**哈希表**。查找、插入、删除平均 O(1)。Python 3.7+ 保证保持插入顺序。

```python
# ===== 创建 =====
user = {"name": "Alice", "age": 25, "city": "Beijing"}
empty = {}
from_pairs = dict([("a", 1), ("b", 2)])    # 从键值对列表
from_keys = dict.fromkeys(["a", "b", "c"], 0)  # {'a':0, 'b':0, 'c':0}

# ===== 访问 =====
user["name"]                  # "Alice"（键不存在会 KeyError）
user.get("email", "N/A")     # "N/A"（键不存在返回默认值，不报错）
user.get("email")             # None（不传默认值就返回 None）

# setdefault：键不存在时设置默认值并返回
user.setdefault("tags", [])   # 键不存在 → 设置 [] 并返回
user["tags"].append("vip")    # 安全地操作

# ===== 修改 =====
user["email"] = "a@b.com"    # 新增或修改
user.update({"age": 26, "phone": "123"})  # 批量更新
user |= {"age": 26}           # Python 3.9+ 合并运算符

del user["city"]               # 删除（键不存在会 KeyError）
popped = user.pop("age")      # 弹出（键不存在会 KeyError）
popped = user.pop("age", None) # 弹出并指定默认值（不报错）

# ===== 遍历 =====
for key in user:                # 遍历键（最常用）
    print(key)

for key, value in user.items(): # 遍历键值对
    print(f"{key}: {value}")

for value in user.values():     # 遍历值
    print(value)

# 遍历中修改字典 → 必须先转成列表
for key in list(user.keys()):
    if key.startswith("_"):
        del user[key]

# ===== 字典推导式 =====
squared = {x: x**2 for x in range(5)}
# {0: 0, 1: 1, 2: 4, 3: 9, 4: 16}

# 翻转键值
flipped = {v: k for k, v in {"a": 1, "b": 2}.items()}
# {1: 'a', 2: 'b'}

# 过滤
filtered = {k: v for k, v in user.items() if v is not None}

# ===== 合并字典 =====
d1 = {"a": 1, "b": 2}
d2 = {"b": 3, "c": 4}

# Python 3.9+（推荐）
merged = d1 | d2    # {'a': 1, 'b': 3, 'c': 4}（后者覆盖前者）

# Python 3.5+
merged = {**d1, **d2}

# 旧版
merged = d1.copy()
merged.update(d2)

# ===== 嵌套字典 =====
config = {
    "database": {
        "host": "localhost",
        "port": 5432,
    },
    "redis": {
        "host": "localhost",
        "port": 6379,
    },
}

# 安全访问嵌套字典
db_host = config.get("database", {}).get("host", "localhost")

# ===== defaultdict（自动初始化的字典） =====
from collections import defaultdict

# 按类别分组
groups = defaultdict(list)
for item in ["apple", "banana", "cherry", "avocado"]:
    groups[item[0]].append(item)
# {'a': ['apple', 'avocado'], 'b': ['banana'], 'c': ['cherry']}

# 计数
counter = defaultdict(int)
for word in ["apple", "banana", "apple", "cherry", "apple"]:
    counter[word] += 1
# {'apple': 3, 'banana': 1, 'cherry': 1}
# 其实直接用 collections.Counter 更方便

# ===== OrderedDict（需要精确控制顺序时用） =====
from collections import OrderedDict
# Python 3.7+ 普通 dict 已经保序，OrderedDict 的额外价值：
# - move_to_end() 移动键到末尾/开头
# - popitem(last=True/False) 从末尾/开头弹出
# - 相等性比较考虑顺序
```

> **dict 键的要求**：键必须是**可哈希的**（hashable）。不可变类型（str, int, float, tuple, frozenset）可以，可变类型（list, dict, set）不行。

### 2.4 集合 set

集合是**无序、不重复**的元素容器，底层也是哈希表。主要用于：去重、成员测试（O(1)）、集合运算。

```python
# ===== 创建 =====
fruits = {"apple", "banana", "cherry"}
from_list = set([1, 2, 2, 3, 3, 3])   # {1, 2, 3}
empty = set()     # 注意：{} 创建的是空字典，不是空集合！

# ===== 增删 =====
fruits.add("orange")
fruits.update(["grape", "mango"])    # 添加多个
fruits.remove("banana")              # 删除（不存在会 KeyError）
fruits.discard("xyz")                # 删除（不存在不报错）
fruits.pop()                         # 随机弹出一个

# ===== 成员测试（比 list 快得多） =====
"apple" in fruits    # O(1)，而 list 是 O(n)

# 当需要频繁判断"某元素是否存在"时，把 list 转成 set
valid_ids = set([1, 5, 10, 20, 50])
if user_id in valid_ids:    # 比 list 快几个数量级
    ...

# ===== 集合运算 =====
a = {1, 2, 3, 4}
b = {3, 4, 5, 6}

a | b    # {1, 2, 3, 4, 5, 6}   并集（a.union(b)）
a & b    # {3, 4}                交集（a.intersection(b)）
a - b    # {1, 2}                差集（a.difference(b)）
b - a    # {5, 6}                差集方向不同
a ^ b    # {1, 2, 5, 6}          对称差集（不共有的元素）

# 子集/超集
{1, 2} <= {1, 2, 3}     # True（子集）
{1, 2, 3} >= {1, 2}     # True（超集）

# ===== 实际应用 =====
# 1. 列表去重
nums = [1, 2, 2, 3, 3, 3, 4]
unique = list(set(nums))   # [1, 2, 3, 4]（注意：顺序可能变）

# 保序去重（Python 3.7+）
unique_ordered = list(dict.fromkeys(nums))   # [1, 2, 3, 4]

# 2. 找出两个列表的共同元素 / 差异
list_a = ["Alice", "Bob", "Charlie"]
list_b = ["Bob", "David", "Charlie"]
common = set(list_a) & set(list_b)     # {"Bob", "Charlie"}
only_a = set(list_a) - set(list_b)     # {"Alice"}

# 3. 集合推导式
lengths = {len(word) for word in ["apple", "banana", "cherry", "date"]}
# {5, 6, 4}

# ===== frozenset（不可变集合） =====
fs = frozenset([1, 2, 3])
fs.add(4)    # AttributeError！
# 用途：作为字典的键或放入另一个 set 中
```

---

## 三、函数与模块

### 3.1 函数定义

函数是组织代码的基本单元。Python 的函数是一等公民（first-class），可以赋值给变量、作为参数传递、从函数中返回。

```python
def greet(name: str, greeting: str = "你好") -> str:
    """
    向指定人员打招呼。
    
    参数：
        name: 人员姓名
        greeting: 问候语，默认"你好"
    
    返回：
        完整的问候字符串
    
    示例：
        >>> greet("Alice")
        '你好，Alice！'
        >>> greet("Bob", "早上好")
        '早上好，Bob！'
    """
    return f"{greeting}，{name}！"

# 调用方式
greet("Alice")                    # 位置参数
greet("Bob", "早上好")             # 两个位置参数
greet(name="Charlie")             # 关键字参数
greet(greeting="晚安", name="David")  # 关键字参数可以不按顺序
```

> **docstring 规范**：第一个行是简短描述，空一行后是详细说明。推荐用 Google 风格或 Sphinx 风格的文档格式。IDE 会在调用时显示 docstring。

**返回多个值**

```python
def get_user_info(user_id: int) -> tuple[str, int, str]:
    """返回 (姓名, 年龄, 邮箱)"""
    # ... 查询数据库
    return "Alice", 25, "alice@example.com"

# 解包接收
name, age, email = get_user_info(1)

# 或者接收为 tuple
info = get_user_info(1)
print(info[0])   # "Alice"
```

### 3.2 参数类型详解

Python 函数的参数系统非常灵活，也是最容易让人困惑的部分。参数传递的完整顺序规则：

```
位置参数 → 默认参数 → *args → 关键字参数 → **kwargs
```

```python
def func(a, b, *args, key="default", **kwargs):
    """
    a, b       - 必须的位置参数
    *args      - 额外的位置参数，收集为 tuple
    key        - 关键字参数（有默认值）
    **kwargs   - 额外的关键字参数，收集为 dict
    """
    print(f"a={a}, b={b}")
    print(f"args={args}")           # tuple
    print(f"key={key}")
    print(f"kwargs={kwargs}")       # dict

func(1, 2, 3, 4, 5, key="custom", extra="hello")
# a=1, b=2
# args=(3, 4, 5)
# key=custom
# kwargs={'extra': 'hello'}
```

**参数限定符（Python 3.8+）**

```python
def func(pos_only, /, any_kind, *, kw_only):
    """
    pos_only  - 只能按位置传递（/ 之前）
    any_kind  - 位置或关键字都行（/ 和 * 之间）
    kw_only   - 只能按关键字传递（* 之后）
    """
    pass

func(1, 2, kw_only=3)         # ✅
func(1, any_kind=2, kw_only=3) # ✅
func(pos_only=1, ...)          # ❌ TypeError
func(1, 2, 3)                  # ❌ kw_only 必须用关键字
```

**参数解包**

```python
def add(a, b, c):
    return a + b + c

# 用 * 解包列表/元组为位置参数
args = [1, 2, 3]
add(*args)    # 等价于 add(1, 2, 3)

# 用 ** 解包字典为关键字参数
kwargs = {"a": 1, "b": 2, "c": 3}
add(**kwargs)  # 等价于 add(a=1, b=2, c=3)
```

### 3.3 Lambda 与高阶函数

```python
# lambda 是匿名函数，只能写一行表达式
square = lambda x: x ** 2
add = lambda a, b: a + b
full_name = lambda u: f"{u['first']} {u['last']}"

# lambda 最常见的用途是给 sorted/map/filter 等提供 key 函数
users = [{"name": "Bob", "age": 30}, {"name": "Alice", "age": 25}]
sorted(users, key=lambda u: u["age"])    # 按年龄排序

# ===== map / filter / reduce =====
nums = [1, 2, 3, 4, 5]

# map：对每个元素应用函数
doubled = list(map(lambda x: x * 2, nums))    # [2, 4, 6, 8, 10]
# 更推荐用推导式：
doubled = [x * 2 for x in nums]               # 同样的效果，更 Pythonic

# filter：过滤元素
evens = list(filter(lambda x: x % 2 == 0, nums))  # [2, 4]
# 更推荐用推导式：
evens = [x for x in nums if x % 2 == 0]

# reduce：累积归约（需要从 functools 导入）
from functools import reduce
total = reduce(lambda a, b: a + b, nums)       # 15
# 等价于 sum(nums)，但 reduce 可以做更复杂的归约

# ===== functools 常用工具 =====
from functools import partial, lru_cache, wraps

# partial：固定部分参数，创建新函数
def power(base, exponent):
    return base ** exponent

square = partial(power, exponent=2)
cube = partial(power, exponent=3)
square(5)   # 25
cube(5)     # 125

# lru_cache：函数结果缓存（记忆化，对递归和重复计算非常有用）
@lru_cache(maxsize=128)
def fibonacci(n):
    if n < 2:
        return n
    return fibonacci(n - 1) + fibonacci(n - 2)

fibonacci(100)   # 瞬间完成，没有缓存会算到天荒地老
```

### 3.4 闭包

闭包 = 内层函数 + 它引用的外层函数变量。闭包让函数能"记住"创建时的环境。

```python
def make_multiplier(factor):
    """创建一个乘法函数"""
    def multiplier(x):
        return x * factor    # factor 来自外层函数（被"闭合"捕获）
    return multiplier

double = make_multiplier(2)
triple = make_multiplier(3)

double(5)    # 10
triple(5)    # 15

# 闭包的经典应用：计数器
def make_counter():
    count = 0
    def counter():
        nonlocal count     # 声明要修改外层变量
        count += 1
        return count
    return counter

c = make_counter()
c()    # 1
c()    # 2
c()    # 3
```

> **闭包陷阱**：循环中创建闭包时，所有闭包共享同一个变量的引用。
> ```python
> # 错误：所有函数都返回最后一个 i 的值
> funcs = [lambda: i for i in range(5)]
> [f() for f in funcs]   # [4, 4, 4, 4, 4]
>
> # 正确：用默认参数绑定当前值
> funcs = [lambda i=i: i for i in range(5)]
> [f() for f in funcs]   # [0, 1, 2, 3, 4]
> ```

### 3.5 模块与包

```python
# ===== 导入方式 =====
import os                           # 导入整个模块
from pathlib import Path            # 导入特定对象
from datetime import datetime as dt # 别名
from mypackage import func1, func2  # 从包导入

# ===== __name__ 的作用 =====
# 每个 Python 文件都有 __name__ 属性
# 直接运行时 __name__ == "__main__"
# 被 import 时 __name__ == 模块名

if __name__ == "__main__":
    # 只有直接运行此文件时才执行，被 import 时不执行
    # 常用于测试代码或启动入口
    main()

# ===== __all__ 控制 from module import * =====
# 在模块顶部定义 __all__，指定 import * 时导出哪些名字
__all__ = ["public_func", "PublicClass"]

def public_func():    # 会被导出
    pass

def _internal():      # 不会导出（下划线开头也不会）
    pass

# ===== 包结构 =====
# mypackage/
# ├── __init__.py        # 包的初始化（可以为空，也可以放初始化代码）
# ├── module_a.py        # from mypackage import module_a
# ├── module_b.py        # from mypackage.module_b import some_func
# └── subpackage/
#     ├── __init__.py
#     └── module_c.py    # from mypackage.subpackage.module_c import ...

# ===== 避免循环导入 =====
# a.py: from b import func_b
# b.py: from a import func_a  → ImportError!
#
# 解决方案：
# 1. 重构：把共用代码提取到第三个模块 c.py
# 2. 延迟导入：在函数内部 import（不推荐）
# 3. 用 import module 代替 from module import name
```

---

## 四、面向对象编程

### 4.1 类的基本定义

```python
class Animal:
    """动物基类 —— 演示类的核心概念"""
    
    # 类变量：所有实例共享（谨慎使用，通常用常量）
    kingdom = "动物界"
    _count = 0    # 下划线开头表示"私有"（约定，非强制）
    
    def __init__(self, name: str, age: int):
        """构造方法：创建实例时自动调用"""
        self.name = name          # 实例属性（公有）
        self._internal = "约定私有"  # 约定私有（外部仍可以访问）
        self.__private = "名称改写"  # 双下划线触发 name mangling
        Animal._count += 1
    
    def speak(self) -> str:
        """实例方法：第一个参数必须是 self"""
        return f"{self.name}在叫"
    
    def __str__(self) -> str:
        """print(obj) 时调用"""
        return f"Animal({self.name}, {self.age}岁)"
    
    def __repr__(self) -> str:
        """在 REPL 中直接显示 obj 时调用，应返回可重建对象的字符串"""
        return f"Animal(name={self.name!r}, age={self.age})"

# 创建实例
dog = Animal("旺财", 3)
print(dog)          # Animal(旺财, 3岁)
repr(dog)           # "Animal(name='旺财', age=3)"
```

### 4.2 继承与多态

```python
class Dog(Animal):
    def __init__(self, name: str, age: int, breed: str):
        super().__init__(name, age)   # 调用父类构造
        self.breed = breed
    
    def speak(self) -> str:           # 方法重写
        return f"{self.name}：汪汪汪！"

class Cat(Animal):
    def speak(self) -> str:
        return f"{self.name}：喵喵喵~"

# 多态：同一个接口，不同实现
animals: list[Animal] = [Dog("旺财", 3, "柴犬"), Cat("咪咪", 2)]
for animal in animals:
    print(animal.speak())    # 不需要知道具体类型，各自调用自己的 speak

# isinstance 检查（考虑继承）
isinstance(dog, Dog)       # True
isinstance(dog, Animal)    # True（子类也是父类类型）
```

### 4.3 类方法、静态方法、属性

```python
class Circle:
    _pi = 3.14159
    
    def __init__(self, radius: float):
        self._radius = radius
    
    @property
    def radius(self) -> float:
        """像属性一样访问，实际执行方法（getter）"""
        return self._radius
    
    @radius.setter
    def radius(self, value: float):
        """赋值时触发（setter），可以加验证逻辑"""
        if value < 0:
            raise ValueError("半径不能为负")
        self._radius = value
    
    @property
    def area(self) -> float:
        """只读属性（只有 getter 没有 setter）"""
        return self._pi * self._radius ** 2
    
    @classmethod
    def from_diameter(cls, diameter: float) -> "Circle":
        """工厂方法：用类方法创建实例的替代构造方式"""
        return cls(diameter / 2)
    
    @staticmethod
    def is_valid_radius(value: float) -> bool:
        """静态方法：不需要 self 或 cls，纯粹是工具函数"""
        return value >= 0

c = Circle(5)
c.radius        # 5（触发 getter）
c.radius = 10   # 触发 setter
c.area          # 314.159（只读属性）
c2 = Circle.from_diameter(10)   # 工厂方法
```

### 4.4 dataclass（Python 3.7+，强烈推荐）

当你需要一个主要用来存储数据的类时，`@dataclass` 可以省去大量样板代码。它会自动生成 `__init__`、`__repr__`、`__eq__` 等方法。

```python
from dataclasses import dataclass, field

@dataclass
class User:
    name: str
    age: int
    email: str = ""
    tags: list[str] = field(default_factory=list)
    
    @property
    def is_adult(self) -> bool:
        return self.age >= 18

# 自动生成 __init__、__repr__、__eq__
u = User("Alice", 25, "a@b.com")
print(u)   # User(name='Alice', age=25, email='a@b.com', tags=[])

# 常用选项
@dataclass(frozen=True)    # 不可变（类似 namedtuple 但更灵活）
class Point:
    x: float
    y: float

@dataclass(order=True)     # 自动生成 __lt__ __le__ __gt__ __ge__
class Student:
    grade: float
    name: str

# frozen=True 后可以做 dict 的 key
p = Point(1.0, 2.0)
d = {p: "origin area"}
```

### 4.5 魔术方法（dunder methods）

魔术方法让你的类能像内置类型一样工作。

```python
class Vector:
    def __init__(self, x: float, y: float):
        self.x = x
        self.y = y
    
    def __repr__(self):
        return f"Vector({self.x}, {self.y})"
    
    def __add__(self, other):          # v1 + v2
        return Vector(self.x + other.x, self.y + other.y)
    
    def __mul__(self, scalar):         # v * 3
        return Vector(self.x * scalar, self.y * scalar)
    
    def __abs__(self):                 # abs(v)
        return (self.x**2 + self.y**2) ** 0.5
    
    def __eq__(self, other):           # v1 == v2
        return self.x == other.x and self.y == other.y
    
    def __len__(self):                 # len(v) —— 语义自定义
        return 2
    
    def __getitem__(self, idx):        # v[0], v[1]
        return (self.x, self.y)[idx]
    
    def __iter__(self):                # 支持 for x in v
        yield self.x
        yield self.y
    
    def __contains__(self, value):     # 0 in v
        return value in (self.x, self.y)

v1 = Vector(1, 2)
v2 = Vector(3, 4)
v1 + v2       # Vector(4, 6)
v1 * 3        # Vector(3, 6)
abs(v2)       # 5.0
v1[0]         # 1
list(v1)      # [1, 2]
```

> **常用魔术方法速查**：
> - 字符串：`__str__`（用户友好）、`__repr__`（开发者友好）
> - 比较：`__eq__`、`__lt__`、`__le__`、`__gt__`、`__ge__`
> - 算术：`__add__`、`__sub__`、`__mul__`、`__truediv__`、`__floordiv__`、`__mod__`
> - 容器：`__len__`、`__getitem__`、`__setitem__`、`__contains__`、`__iter__`
> - 上下文：`__enter__`、`__exit__`（支持 with 语句）
> - 调用：`__call__`（让实例可以像函数一样被调用）

### 4.6 抽象基类

```python
from abc import ABC, abstractmethod

class Shape(ABC):
    """抽象基类：定义接口规范，子类必须实现所有抽象方法"""
    
    @abstractmethod
    def area(self) -> float:
        """计算面积（子类必须实现）"""
        pass
    
    @abstractmethod
    def perimeter(self) -> float:
        """计算周长（子类必须实现）"""
        pass
    
    # 可以有普通方法作为默认实现
    def describe(self) -> str:
        return f"{self.__class__.__name__}: 面积={self.area():.2f}"

class Rectangle(Shape):
    def __init__(self, width: float, height: float):
        self.width = width
        self.height = height
    
    def area(self) -> float:
        return self.width * self.height
    
    def perimeter(self) -> float:
        return 2 * (self.width + self.height)

# Shape()  # TypeError! 不能实例化抽象类
r = Rectangle(3, 4)
r.describe()   # "Rectangle: 面积=12.00"
```

---

## 五、文件与 IO

### 5.1 文件读写

```python
# ===== 推荐写法：with 语句（自动关闭，即使发生异常） =====
with open("data.txt", "r", encoding="utf-8") as f:
    content = f.read()           # 读取全部内容为一个字符串

# 逐行读取（内存友好，适合大文件）
with open("data.txt", "r", encoding="utf-8") as f:
    for line in f:
        process(line.strip())

# 读取所有行为列表
with open("data.txt", "r", encoding="utf-8") as f:
    lines = f.readlines()        # 每行包含 \n，需要 strip()

# ===== 写入 =====
with open("output.txt", "w", encoding="utf-8") as f:
    f.write("第一行\n")
    f.writelines(["第二行\n", "第三行\n"])

# 追加模式
with open("log.txt", "a", encoding="utf-8") as f:
    f.write("新日志\n")

# ===== 文件模式 =====
# "r"  只读（默认）
# "w"  写入（覆盖已有内容！）
# "a"  追加
# "x"  创建新文件（文件已存在会报错）
# "b"  二进制模式（配合 rb/wb 读写图片等）
# "r+" 读写
```

> **编码问题**：Windows 默认编码是 GBK，Linux/Mac 是 UTF-8。**永远显式指定 `encoding="utf-8"`**，否则跨平台时会出乱码。

### 5.2 pathlib（现代路径操作，推荐）

`pathlib` 是 Python 3 推荐的路径操作库，比 `os.path` 更优雅。

```python
from pathlib import Path

# ===== 创建路径 =====
p = Path("data/subdir/file.txt")
p = Path.home() / "documents" / "report.pdf"    # 用 / 拼接路径
p = Path.cwd()                                    # 当前工作目录

# ===== 路径组成部分 =====
p = Path("/home/user/data/file.txt")
p.name       # "file.txt"（文件名）
p.stem       # "file"（不含扩展名）
p.suffix     # ".txt"（扩展名）
p.parent     # Path("/home/user/data")

# ===== 查询 =====
p.exists()          # 是否存在
p.is_file()         # 是否为文件
p.is_dir()          # 是否为目录
p.stat()            # 文件信息（大小、修改时间等）
p.stat().st_size    # 文件大小（字节）

# ===== 创建/删除 =====
p.parent.mkdir(parents=True, exist_ok=True)   # 创建目录（含父级）
p.touch()                                      # 创建空文件
p.unlink()                                     # 删除文件
Path("empty_dir").rmdir()                      # 删除空目录

# ===== 遍历目录 =====
for item in Path(".").iterdir():        # 当前目录下的文件和文件夹
    print(item)

for py_file in Path(".").glob("*.py"):  # 当前目录下的 .py 文件
    print(py_file)

for py_file in Path(".").glob("**/*.py"):  # 递归查找所有 .py
    print(py_file)

# ===== 读写快捷方法 =====
Path("data.txt").write_text("内容", encoding="utf-8")
text = Path("data.txt").read_text(encoding="utf-8")
data = Path("data.bin").read_bytes()
```

### 5.3 JSON / CSV

```python
import json
import csv

# ===== JSON =====
data = {"name": "Alice", "scores": [90, 85, 92], "active": True}

# 序列化
json_str = json.dumps(data, ensure_ascii=False, indent=2)
# ensure_ascii=False 才能正确输出中文
# indent=2 格式化输出（生产环境不传，节省空间）

# 反序列化
parsed = json.loads(json_str)

# 文件读写
with open("data.json", "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=2)

with open("data.json", "r", encoding="utf-8") as f:
    data = json.load(f)

# 处理日期等特殊类型
from datetime import datetime
class DateEncoder(json.JSONEncoder):
    def default(self, obj):
        if isinstance(obj, datetime):
            return obj.isoformat()
        return super().default(obj)

json.dumps({"now": datetime.now()}, cls=DateEncoder)

# ===== CSV =====
# 写入
with open("data.csv", "w", newline="", encoding="utf-8-sig") as f:
    writer = csv.writer(f)
    writer.writerow(["姓名", "年龄", "城市"])
    writer.writerow(["Alice", 25, "北京"])
    writer.writerows([["Bob", 30, "上海"], ["Charlie", 35, "广州"]])

# 读取
with open("data.csv", "r", encoding="utf-8-sig") as f:
    reader = csv.DictReader(f)    # 用第一行做键名
    for row in reader:
        print(row["姓名"], row["年龄"])

# 注意：encoding="utf-8-sig" 可以正确处理带 BOM 的 CSV（Excel 保存的）
```

---

## 六、异常处理

### 6.1 基本语法

```python
try:
    result = 10 / 0
except ZeroDivisionError as e:
    print(f"除零错误：{e}")
except (TypeError, ValueError) as e:
    print(f"类型或值错误：{e}")
except Exception as e:
    # 兜底：捕获所有常规异常（不推荐滥用）
    print(f"其他错误：{e}")
else:
    # 没有异常时执行（很少用但很有用）
    print("计算成功")
finally:
    # 始终执行（清理资源）
    print("清理完毕")
```

### 6.2 异常层次结构

```
BaseException
├── SystemExit          # sys.exit() 触发
├── KeyboardInterrupt   # Ctrl+C
├── GeneratorExit       # 生成器关闭
└── Exception           # 所有常规异常的基类
    ├── ValueError
    ├── TypeError
    ├── KeyError
    ├── IndexError
    ├── FileNotFoundError
    ├── IOError / OSError
    └── ...
```

> **最佳实践**：只捕获 `Exception` 及其子类，不要裸 `except:` 或 `except BaseException:`，否则会吞掉 `SystemExit` 和 `KeyboardInterrupt`。

### 6.3 自定义异常

```python
class BizException(Exception):
    """业务异常基类"""
    def __init__(self, message: str, code: int = 400):
        super().__init__(message)
        self.code = code
        self.message = message

class NotFoundException(BizException):
    def __init__(self, resource: str, id: int):
        super().__init__(f"{resource} #{id} 不存在", code=404)

class ValidationError(BizException):
    def __init__(self, field: str, reason: str):
        super().__init__(f"字段 {field} 验证失败：{reason}", code=422)

# 使用
raise NotFoundException("用户", 42)
raise ValidationError("email", "格式不正确")

# 捕获特定业务异常
try:
    process_order(order_id)
except NotFoundException as e:
    return {"error": e.message}, e.code
except ValidationError as e:
    return {"error": e.message, "field": "order"}, e.code
except BizException as e:
    return {"error": e.message}, e.code
```

### 6.4 上下文管理器

```python
# 类实现
class FileManager:
    def __init__(self, filename, mode="r"):
        self.filename = filename
        self.mode = mode
    
    def __enter__(self):
        self.file = open(self.filename, self.mode, encoding="utf-8")
        return self.file
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        self.file.close()
        return False  # 不吞掉异常

with FileManager("data.txt") as f:
    content = f.read()

# contextlib 简化写法
from contextlib import contextmanager

@contextmanager
def timer(label: str):
    """计时上下文管理器"""
    import time
    start = time.perf_counter()
    yield  # 进入 with 块
    elapsed = time.perf_counter() - start
    print(f"{label}: {elapsed:.4f}s")

with timer("数据库查询"):
    query_database()

@contextmanager
def temp_directory():
    """临时目录：退出时自动删除"""
    import tempfile, shutil
    tmpdir = tempfile.mkdtemp()
    try:
        yield tmpdir
    finally:
        shutil.rmtree(tmpdir, ignore_errors=True)
```

---

## 七、进阶特性

### 7.1 装饰器

装饰器是 Python 最强大的特性之一。本质是一个接收函数并返回函数的高阶函数。

```python
import functools
import time

# ===== 基本装饰器 =====
def timer(func):
    """计算函数执行时间"""
    @functools.wraps(func)   # 保留原函数的 __name__ 和 __doc__
    def wrapper(*args, **kwargs):
        start = time.perf_counter()
        result = func(*args, **kwargs)
        elapsed = time.perf_counter() - start
        print(f"{func.__name__} 耗时 {elapsed:.4f}s")
        return result
    return wrapper

@timer    # 等价于 slow_function = timer(slow_function)
def slow_function():
    time.sleep(1)
    return "done"

# ===== 带参数的装饰器 =====
def retry(max_attempts=3, delay=1):
    """失败重试装饰器"""
    def decorator(func):
        @functools.wraps(func)
        def wrapper(*args, **kwargs):
            for attempt in range(max_attempts):
                try:
                    return func(*args, **kwargs)
                except Exception as e:
                    if attempt == max_attempts - 1:
                        raise
                    print(f"第 {attempt+1} 次失败：{e}，{delay}s 后重试...")
                    time.sleep(delay)
        return wrapper
    return decorator

@retry(max_attempts=5, delay=2)
def unstable_api_call():
    pass

# ===== 类装饰器 =====
def singleton(cls):
    """让类变成单例模式"""
    instances = {}
    @functools.wraps(cls)
    def get_instance(*args, **kwargs):
        if cls not in instances:
            instances[cls] = cls(*args, **kwargs)
        return instances[cls]
    return get_instance

@singleton
class Database:
    def __init__(self):
        print("创建数据库连接...")

db1 = Database()   # 创建数据库连接...
db2 = Database()   # 不输出（复用 db1）
db1 is db2         # True
```

> **`@functools.wraps` 必须加**：不加的话，被装饰的函数的 `__name__`、`__doc__` 会变成 wrapper 的，导致调试困难、文档丢失。

### 7.2 生成器与迭代器

```python
# ===== 生成器函数 =====
def fibonacci(n):
    """生成斐波那契数列"""
    a, b = 0, 1
    for _ in range(n):
        yield a
        a, b = b, a + b

list(fibonacci(10))   # [0, 1, 1, 2, 3, 5, 8, 13, 21, 34]

# 生成器的核心价值：惰性求值，不一次性加载所有数据到内存
def read_large_file(filepath):
    """逐行读取大文件（不会把整个文件加载到内存）"""
    with open(filepath, "r", encoding="utf-8") as f:
        for line in f:
            yield line.strip()

# 处理 10GB 的文件也不会 OOM
for line in read_large_file("huge_data.csv"):
    process(line)

# ===== 生成器表达式 =====
squares_list = [x**2 for x in range(1000000)]    # 列表：立即占用大量内存
squares_gen  = (x**2 for x in range(1000000))    # 生成器：几乎不占内存

# ===== yield from（委托给另一个生成器） =====
def flatten(nested):
    """展平嵌套列表"""
    for item in nested:
        if isinstance(item, list):
            yield from flatten(item)    # 委托给递归调用
        else:
            yield item

list(flatten([1, [2, 3], [4, [5, 6]]]))   # [1, 2, 3, 4, 5, 6]
```

### 7.3 类型提示进阶

```python
from typing import TypeVar, Generic, Protocol, Callable, Any
from collections.abc import Sequence, Iterator

# ===== 泛型 =====
T = TypeVar("T")

class Stack(Generic[T]):
    def __init__(self):
        self._items: list[T] = []
    
    def push(self, item: T) -> None:
        self._items.append(item)
    
    def pop(self) -> T:
        return self._items.pop()
    
    def peek(self) -> T:
        return self._items[-1]

int_stack = Stack[int]()
int_stack.push(42)
# int_stack.push("hello")  # mypy 会报错

# ===== Protocol（鸭子类型的形式化） =====
class Drawable(Protocol):
    def draw(self) -> None:
        ...

def render(obj: Drawable):   # 任何有 draw() 方法的对象都能传入
    obj.draw()

# 不需要显式继承 Drawable，只要有 draw 方法就行
class Circle:
    def draw(self) -> None:
        print("画圆")

render(Circle())   # ✅ 类型检查通过

# ===== 常用类型别名 =====
from typing import Union, Optional

def process(value: str | int | None) -> str:    # Python 3.10+ 联合类型
    if value is None:
        return "空"
    return str(value)

# 回调类型
def apply(func: Callable[[int, int], int], a: int, b: int) -> int:
    return func(a, b)

apply(lambda x, y: x + y, 3, 4)   # 7
```

---

## 八、并发编程

### 8.1 GIL（全局解释器锁）

Python 的 GIL 是理解并发编程的关键：**同一时刻只有一个线程执行 Python 字节码**。

- **CPU 密集型**（计算多）：用 `multiprocessing`（多进程，绕过 GIL）
- **IO 密集型**（网络/文件操作多）：用 `threading` 或 `asyncio`（IO 等待时释放 GIL）

### 8.2 线程池与进程池

```python
from concurrent.futures import ThreadPoolExecutor, ProcessPoolExecutor
import time

def fetch_url(url: str) -> str:
    """IO 密集型任务"""
    import urllib.request
    with urllib.request.urlopen(url) as resp:
        return resp.read().decode()

# ===== 线程池（IO 密集型） =====
urls = ["https://example.com"] * 10

with ThreadPoolExecutor(max_workers=5) as pool:
    # map：按顺序获取结果
    results = list(pool.map(fetch_url, urls))
    
    # submit：更灵活，返回 Future 对象
    futures = [pool.submit(fetch_url, url) for url in urls]
    results = [f.result() for f in futures]

# ===== 进程池（CPU 密集型） =====
def cpu_heavy(n: int) -> int:
    return sum(i * i for i in range(n))

with ProcessPoolExecutor() as pool:
    results = list(pool.map(cpu_heavy, [10**7] * 4))
```

### 8.3 asyncio 异步编程

```python
import asyncio
import aiohttp

async def fetch(url: str) -> dict:
    """异步 HTTP 请求"""
    async with aiohttp.ClientSession() as session:
        async with session.get(url) as response:
            return await response.json()

async def main():
    urls = [
        "https://api.example.com/data/1",
        "https://api.example.com/data/2",
        "https://api.example.com/data/3",
    ]
    # 并发执行所有请求（比串行快数倍）
    tasks = [fetch(url) for url in urls]
    results = await asyncio.gather(*tasks)
    
    # 带超时控制
    try:
        result = await asyncio.wait_for(fetch(urls[0]), timeout=5.0)
    except asyncio.TimeoutError:
        print("请求超时")

# 运行
asyncio.run(main())
```

---

## 九、标准库精选

### 9.1 日期与时间

```python
from datetime import datetime, date, timedelta, timezone

now = datetime.now()                          # 本地时间
utc_now = datetime.now(timezone.utc)          # UTC 时间
formatted = now.strftime("%Y-%m-%d %H:%M:%S") # 格式化输出
parsed = datetime.strptime("2024-01-15 10:30", "%Y-%m-%d %H:%M")  # 解析字符串

# 时间差
tomorrow = now + timedelta(days=1)
diff = now - datetime(2024, 1, 1)
print(f"已过 {diff.days} 天 {diff.seconds // 3600} 小时")

# 时区处理（Python 3.9+ zoneinfo）
from zoneinfo import ZoneInfo
tokyo = datetime.now(ZoneInfo("Asia/Tokyo"))
shanghai = datetime.now(ZoneInfo("Asia/Shanghai"))
```

### 9.2 正则表达式

```python
import re

text = "联系方式：alice@example.com 或 bob@test.org，电话 13812345678"

# 查找所有邮箱
emails = re.findall(r"[\w.+-]+@[\w-]+\.[\w.]+", text)
# ['alice@example.com', 'bob@test.org']

# 命名分组（比数字索引可读性好得多）
pattern = re.compile(r"(?P<year>\d{4})-(?P<month>\d{2})-(?P<day>\d{2})")
match = pattern.search("日期：2024-01-15")
if match:
    print(match.group("year"))    # "2024"
    print(match.groupdict())      # {'year': '2024', 'month': '01', 'day': '15'}

# 替换
cleaned = re.sub(r"\d+", "X", "订单号：12345，金额：999")
# '订单号：X，金额：X'

# 编译正则（重复使用时性能更好）
email_pattern = re.compile(r"[\w.+-]+@[\w-]+\.[\w.]+")
matches = email_pattern.findall(text)
```

### 9.3 collections 容器工具

```python
from collections import Counter, defaultdict, deque, namedtuple, OrderedDict

# Counter：计数器
words = ["apple", "banana", "apple", "cherry", "apple", "banana"]
counter = Counter(words)
# Counter({'apple': 3, 'banana': 2, 'cherry': 1})
counter.most_common(2)    # [('apple', 3), ('banana', 2)]

# deque：双端队列（两端增删都是 O(1)）
dq = deque([1, 2, 3])
dq.appendleft(0)     # deque([0, 1, 2, 3])
dq.append(4)          # deque([0, 1, 2, 3, 4])
dq.popleft()          # 0
dq.pop()              # 4
dq.rotate(1)          # 右旋一位

# 固定长度的 deque（自动丢弃旧元素）
recent = deque(maxlen=5)
for i in range(10):
    recent.append(i)
# deque([5, 6, 7, 8, 9], maxlen=5)
```

### 9.4 logging 日志

```python
import logging

# 基础配置
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
    handlers=[
        logging.FileHandler("app.log", encoding="utf-8"),
        logging.StreamHandler(),    # 同时输出到控制台
    ],
)

logger = logging.getLogger(__name__)

logger.debug("调试信息")
logger.info("服务启动成功，端口 %d", 8000)
logger.warning("磁盘使用率超过 80%%")
logger.error("数据库连接失败", exc_info=True)   # exc_info=True 附带堆栈
logger.critical("系统即将崩溃")
```

---

## 十、Web 开发

### 10.1 FastAPI（现代高性能，推荐）

FastAPI 是目前最流行的 Python Web 框架，基于类型提示自动生成 API 文档，性能接近 Go/Node.js。

```python
# pip install fastapi uvicorn
from fastapi import FastAPI, HTTPException, Depends, Query
from pydantic import BaseModel, EmailStr, Field
from typing import Optional

app = FastAPI(title="用户管理 API", version="1.0.0")

# ===== 数据模型 =====
class UserCreate(BaseModel):
    name: str = Field(..., min_length=1, max_length=100, description="用户姓名")
    email: EmailStr
    age: Optional[int] = Field(None, ge=0, le=150)

class UserResponse(BaseModel):
    id: int
    name: str
    email: str
    age: Optional[int]

# ===== 路由 =====
@app.get("/api/users/{user_id}", response_model=UserResponse, tags=["用户"])
async def get_user(user_id: int):
    """根据 ID 获取用户详情"""
    # 模拟数据库查询
    user = {"id": user_id, "name": "Alice", "email": "a@b.com", "age": 25}
    if not user:
        raise HTTPException(status_code=404, detail="用户不存在")
    return user

@app.post("/api/users", response_model=UserResponse, status_code=201, tags=["用户"])
async def create_user(user: UserCreate):
    """创建新用户"""
    return {"id": 1, **user.model_dump()}

@app.get("/api/users", tags=["用户"])
async def list_users(
    page: int = Query(1, ge=1, description="页码"),
    size: int = Query(20, ge=1, le=100, description="每页数量"),
    keyword: Optional[str] = Query(None, description="搜索关键词"),
):
    """分页查询用户列表"""
    return {"page": page, "size": size, "items": []}

# 启动：uvicorn main:app --reload --port 8000
# 文档：http://localhost:8000/docs（Swagger UI 自动生成）
# 备选文档：http://localhost:8000/redoc
```

### 10.2 Flask（轻量级）

```python
# pip install flask
from flask import Flask, request, jsonify, render_template

app = Flask(__name__)

@app.route("/")
def index():
    return render_template("index.html")

@app.route("/api/users", methods=["GET"])
def get_users():
    page = request.args.get("page", 1, type=int)
    return jsonify({"users": [], "page": page})

@app.route("/api/users", methods=["POST"])
def create_user():
    data = request.get_json()
    return jsonify({"id": 1, **data}), 201

# 蓝图（模块化）
from flask import Blueprint
user_bp = Blueprint("users", __name__, url_prefix="/api")

@user_bp.route("/users")
def list_users():
    return jsonify([])

app.register_blueprint(user_bp)

if __name__ == "__main__":
    app.run(debug=True, port=5000)
```

---

## 十一、数据科学与 AI

### 11.1 NumPy（数值计算）

```python
import numpy as np

# 创建数组
arr = np.array([1, 2, 3, 4, 5])
matrix = np.zeros((3, 4))          # 3x4 零矩阵
random = np.random.randn(100, 50)  # 标准正态分布 100x50

# 运算（向量化，比 for 循环快 100 倍+）
arr * 2             # 标量乘法 → [2, 4, 6, 8, 10]
arr @ arr           # 点积
np.dot(arr, arr)    # 同上
np.mean(arr)        # 均值
np.std(arr)         # 标准差
np.sum(arr, axis=0) # 按列求和

# 广播机制（不同形状的数组自动对齐运算）
a = np.array([[1], [2], [3]])   # 形状 (3, 1)
b = np.array([10, 20, 30])      # 形状 (3,)
a + b                           # 形状 (3, 3) 自动广播
```

### 11.2 Pandas（数据分析）

```python
import pandas as pd

# 创建 DataFrame
df = pd.DataFrame({
    "姓名": ["Alice", "Bob", "Charlie", "David"],
    "年龄": [25, 30, 35, 28],
    "城市": ["北京", "上海", "广州", "北京"],
    "薪资": [15000, 20000, 18000, 16000],
})

# 查看
df.head(3)         # 前 3 行
df.info()          # 概要（类型、非空计数、内存）
df.describe()      # 统计汇总（均值、标准差、分位数）

# 筛选
df[df["年龄"] > 28]
df[(df["年龄"] > 25) & (df["薪资"] > 16000)]
df[df["城市"].isin(["北京", "上海"])]
df[df["姓名"].str.startswith("A")]    # 字符串方法

# 分组聚合
df.groupby("城市")["薪资"].agg(["mean", "max", "count"])
df.groupby("城市").agg(平均薪资=("薪资", "mean"), 人数=("姓名", "count"))

# 排序
df.sort_values("薪资", ascending=False)

# 新增/修改列
df["年薪"] = df["薪资"] * 12
df["年龄段"] = pd.cut(df["年龄"], bins=[0, 25, 30, 35, 100], labels=["青年", "中青年", "中年", "中老年"])

# 读取/写入
df = pd.read_csv("data.csv", encoding="utf-8")
df = pd.read_excel("data.xlsx", sheet_name="Sheet1")
df.to_csv("output.csv", index=False, encoding="utf-8-sig")
```

### 11.3 机器学习（scikit-learn）

```python
from sklearn.model_selection import train_test_split, cross_val_score
from sklearn.ensemble import RandomForestClassifier, GradientBoostingClassifier
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import classification_report, accuracy_score, confusion_matrix
import numpy as np

# 1. 准备数据
X = np.random.randn(1000, 10)   # 1000 样本，10 特征
y = (X[:, 0] + X[:, 1] > 0).astype(int)  # 二分类标签

# 2. 划分训练集/测试集（80% 训练，20% 测试）
X_train, X_test, y_train, y_test = train_test_split(
    X, y, test_size=0.2, random_state=42, stratify=y  # stratify 保持类别比例
)

# 3. 特征标准化（均值为 0，标准差为 1）
scaler = StandardScaler()
X_train_scaled = scaler.fit_transform(X_train)    # 在训练集上 fit
X_test_scaled = scaler.transform(X_test)           # 在测试集上只 transform

# 4. 训练模型
model = RandomForestClassifier(n_estimators=100, random_state=42, n_jobs=-1)
model.fit(X_train_scaled, y_train)

# 5. 预测与评估
y_pred = model.predict(X_test_scaled)
print(f"准确率：{accuracy_score(y_test, y_pred):.4f}")
print(classification_report(y_test, y_pred))
print("混淆矩阵：\n", confusion_matrix(y_test, y_pred))

# 6. 交叉验证（更可靠的评估）
scores = cross_val_score(model, X_train_scaled, y_train, cv=5)
print(f"交叉验证准确率：{scores.mean():.4f} ± {scores.std():.4f}")

# 7. 特征重要性
importances = model.feature_importances_
for i, imp in enumerate(sorted(enumerate(importances), key=lambda x: -x[1])):
    print(f"特征 {imp[0]}: {imp[1]:.4f}")
```

---

## 十二、数据库操作

### 12.1 SQLAlchemy ORM

```python
# pip install sqlalchemy
from sqlalchemy import create_engine, Column, Integer, String, DateTime, Text
from sqlalchemy.orm import declarative_base, sessionmaker, relationship
from datetime import datetime

# 连接数据库
engine = create_engine("sqlite:///mydb.db", echo=False)
Base = declarative_base()
Session = sessionmaker(bind=engine)

# 定义模型
class User(Base):
    __tablename__ = "users"
    
    id = Column(Integer, primary_key=True, autoincrement=True)
    name = Column(String(100), nullable=False, comment="用户姓名")
    email = Column(String(200), unique=True, comment="邮箱")
    created_at = Column(DateTime, default=datetime.now, comment="创建时间")
    
    # 关联（一对多）
    posts = relationship("Post", back_populates="author")
    
    def __repr__(self):
        return f"<User(id={self.id}, name={self.name!r})>"

class Post(Base):
    __tablename__ = "posts"
    
    id = Column(Integer, primary_key=True)
    title = Column(String(200), nullable=False)
    content = Column(Text)
    author_id = Column(Integer, nullable=False)
    created_at = Column(DateTime, default=datetime.now)
    
    author = relationship("User", back_populates="posts")

# 创建表
Base.metadata.create_all(engine)

# 使用
session = Session()

# 增
new_user = User(name="Alice", email="a@b.com")
session.add(new_user)
session.commit()

# 查
user = session.query(User).filter_by(name="Alice").first()
users = session.query(User).filter(User.name.like("%Ali%")).all()
users = session.query(User).order_by(User.created_at.desc()).limit(10).all()

# 改
user.email = "new_email@example.com"
session.commit()

# 删
session.delete(user)
session.commit()

session.close()
```

---

## 十三、测试与调试

### 13.1 pytest

```python
# pip install pytest pytest-cov

# test_calculator.py
import pytest

def add(a: float, b: float) -> float:
    return a + b

def divide(a: float, b: float) -> float:
    if b == 0:
        raise ValueError("除数不能为零")
    return a / b

# 基本测试
def test_add():
    assert add(1, 2) == 3
    assert add(-1, 1) == 0
    assert add(0.1, 0.2) == pytest.approx(0.3)    # 浮点数比较

# 参数化测试（一组数据跑同一个测试）
@pytest.mark.parametrize("a, b, expected", [
    (1, 2, 3),
    (0, 0, 0),
    (-1, -1, -2),
    (100, 200, 300),
])
def test_add_parametrized(a, b, expected):
    assert add(a, b) == expected

# 异常测试
def test_divide_by_zero():
    with pytest.raises(ValueError, match="除数不能为零"):
        divide(1, 0)

# fixture（测试前的准备工作）
@pytest.fixture
def sample_users():
    return [{"name": "Alice", "age": 25}, {"name": "Bob", "age": 30}]

def test_user_count(sample_users):
    assert len(sample_users) == 2

# 运行：pytest -v --cov=my_module --cov-report=html
```

### 13.2 调试技巧

```python
# breakpoint()（Python 3.7+，推荐）
def buggy_function(data):
    breakpoint()   # 在此处暂停，进入 pdb 交互式调试
    result = process(data)
    return result

# pdb 常用命令：
# n (next)      - 执行下一行
# s (step)      - 进入函数内部
# c (continue)  - 继续运行到下一个断点
# p variable    - 打印变量值
# l (list)      - 查看当前代码上下文
# q (quit)      - 退出调试
# h (help)      - 查看帮助

# rich 库美化输出
from rich import print as rprint
from rich.traceback import install
install()   # 美化异常堆栈，高亮显示错误位置
rprint("[bold green]成功[/] 处理了 [cyan]{count}[/] 条数据")
```

---

## 十四、工程化实践

### 14.1 项目结构

```
my_project/
├── pyproject.toml          # 项目元数据与依赖（推荐替代 setup.py）
├── requirements.txt        # 锁定依赖版本
├── .env                    # 环境变量（不入版本控制）
├── .gitignore
├── README.md
├── src/
│   └── my_package/
│       ├── __init__.py
│       ├── main.py         # 应用入口
│       ├── config.py       # 配置管理
│       ├── models/         # 数据模型
│       ├── services/       # 业务逻辑
│       ├── api/            # 路由/控制器
│       └── utils/          # 工具函数
├── tests/
│   ├── conftest.py         # 共享 fixture
│   ├── test_models.py
│   └── test_services.py
├── scripts/                # 运维脚本
│   └── init_db.py
└── docker/
    ├── Dockerfile
    └── docker-compose.yml
```

### 14.2 配置管理

```python
# pip install pydantic-settings
from pydantic_settings import BaseSettings

class Settings(BaseSettings):
    app_name: str = "MyApp"
    debug: bool = False
    database_url: str
    redis_url: str = "redis://localhost:6379"
    jwt_secret: str
    
    class Config:
        env_file = ".env"           # 从 .env 文件读取
        env_file_encoding = "utf-8"

settings = Settings()
# 优先级：环境变量 > .env 文件 > 默认值
```

### 14.3 代码质量工具

```bash
# Ruff（超快的 linter + formatter，替代 flake8 + black + isort）
pip install ruff
ruff format .                    # 格式化代码
ruff check . --fix               # 检查并自动修复

# 类型检查
pip install mypy
mypy src/                        # 静态类型检查

# 预提交钩子（每次 git commit 前自动检查）
pip install pre-commit
# 在 .pre-commit-config.yaml 中配置后：
pre-commit install
```

### 14.4 Docker 部署

```dockerfile
# Dockerfile
FROM python:3.12-slim AS base

WORKDIR /app

# 先复制依赖文件（利用 Docker 缓存层）
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# 再复制源代码
COPY src/ ./src/

EXPOSE 8000

CMD ["uvicorn", "my_package.main:app", "--host", "0.0.0.0", "--port", "8000"]
```

```yaml
# docker-compose.yml
version: "3.8"
services:
  api:
    build: .
    ports:
      - "8000:8000"
    environment:
      - DATABASE_URL=postgresql://user:pass@db:5432/mydb
      - REDIS_URL=redis://redis:6379
    depends_on:
      - db
      - redis
  
  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: user
      POSTGRES_PASSWORD: pass
      POSTGRES_DB: mydb
    volumes:
      - pgdata:/var/lib/postgresql/data
  
  redis:
    image: redis:7-alpine

volumes:
  pgdata:
```

### 14.5 常用第三方库速查

| 领域 | 库 | 用途 |
|------|-----|------|
| HTTP 请求 | `httpx`, `requests` | 同步/异步 HTTP 客户端 |
| CLI 工具 | `click`, `typer` | 命令行参数解析 |
| 数据校验 | `pydantic` | 数据模型与校验 |
| 任务队列 | `celery`, `rq` | 异步任务处理 |
| 缓存 | `redis`, `cachetools` | 缓存与速率限制 |
| 日志 | `loguru` | 更友好的日志库 |
| 进度条 | `tqdm`, `rich` | 循环进度显示 |
| 配置 | `pydantic-settings` | 环境变量管理 |
| 模板 | `jinja2` | HTML/文本模板 |
| 加密 | `cryptography` | 加密/解密/签名 |
| 图像 | `Pillow` | 图像处理 |
| PDF | `reportlab`, `PyPDF2` | PDF 生成与操作 |
| 爬虫 | `scrapy`, `beautifulsoup4` | 网页抓取 |
| 自动化 | `selenium`, `playwright` | 浏览器自动化 |
| ORM | `sqlalchemy` | 数据库 ORM |
| 异步框架 | `fastapi`, `starlette` | 高性能 Web |
| 全功能框架 | `django` | 企业级 Web |
| 轻量框架 | `flask` | 小型 Web 应用 |
| 数据科学 | `numpy`, `pandas` | 数据处理 |
| 可视化 | `matplotlib`, `seaborn`, `plotly` | 图表绘制 |
| 机器学习 | `scikit-learn` | 传统 ML |
| 深度学习 | `pytorch`, `tensorflow` | 神经网络 |
| 桌面应用 | `PyQt6`, `customtkinter` | GUI 应用 |

---

## 十五、学习资源与路线图

### 15.1 学习路线图

```
入门（1-2 周）
├── Python 语法基础（变量、类型、运算符）
├── 数据结构（list/dict/tuple/set）
├── 控制流（if/for/while）
├── 函数定义与调用
└── 文件读写

进阶（2-4 周）
├── 面向对象编程（类、继承、魔术方法）
├── 装饰器与生成器
├── 异常处理与上下文管理器
├── 标准库常用模块
├── 类型提示
└── 虚拟环境与包管理

实战（4-8 周，选一个方向深入）
├── Web 开发：FastAPI / Django / Flask
├── 数据科学：NumPy / Pandas / Matplotlib
├── 自动化脚本：爬虫 / 办公自动化 / 系统管理
├── AI/ML：scikit-learn / PyTorch / TensorFlow
└── 桌面应用：PyQt / Tkinter / DearPyGui

工程化（持续）
├── 测试（pytest）
├── 代码质量（ruff / mypy）
├── 容器化（Docker）
├── CI/CD（GitHub Actions）
├── 性能优化（cProfile / asyncio）
└── 日志与监控
```

### 15.2 推荐资源

**官方文档**
- Python 官方文档（中文）：https://docs.python.org/zh-cn/3/
- Python 教程（官方）：https://docs.python.org/zh-cn/3/tutorial/

**书籍**
- 《Python Crash Course》（Python 编程从入门到实践）— 入门首选
- 《Fluent Python》（流畅的 Python）— 进阶必读，深入理解 Python 特性
- 《Effective Python》— 90 条最佳实践
- 《Python Cookbook》— 经典食谱，按问题分类

**在线平台**
- Real Python：https://realpython.com/ — 高质量教程和文章
- LeetCode：https://leetcode.cn/ — 算法练习
- Kaggle：https://www.kaggle.com/ — 数据科学实战

**视频课程**
- Corey Schafer Python 教程（YouTube）— 讲解清晰
- 廖雪峰 Python 教程 — 中文入门经典

### 15.3 常见陷阱

```python
# 1. 可变默认参数（最常见的坑！）
def append_to(element, target=[]):  # ❌ 所有调用共享同一个列表
    target.append(element)
    return target

append_to(1)   # [1]
append_to(2)   # [1, 2]  ← 不是 [2]！

def append_to(element, target=None):  # ✅ 每次创建新列表
    if target is None:
        target = []
    target.append(element)
    return target

# 2. 浅拷贝陷阱
original = [[1, 2], [3, 4]]
shallow = original.copy()
shallow[0].append(99)   # original 也被修改！

import copy
deep = copy.deepcopy(original)  # ✅ 深拷贝，完全独立

# 3. 整数缓存（is vs ==）
a = 256
b = 256
a is b   # True（-5 到 256 被 Python 缓存）

a = 257
b = 257
a is b   # 可能 False！永远用 == 比较值

# 4. 闭包延迟绑定
funcs = [lambda: i for i in range(5)]
[f() for f in funcs]   # [4, 4, 4, 4, 4] 全是 4！

funcs = [lambda i=i: i for i in range(5)]  # ✅ 默认参数立即绑定
[f() for f in funcs]   # [0, 1, 2, 3, 4]

# 5. 修改遍历中的列表
nums = [1, 2, 3, 4, 5]
for n in nums:
    if n % 2 == 0:
        nums.remove(n)   # ❌ 跳过元素！

# ✅ 正确做法：遍历副本或使用推导式
nums = [n for n in nums if n % 2 != 0]

# 6. GIL 的误解
# CPU 密集型任务用 multiprocessing，不用 threading
# IO 密集型任务用 asyncio 或 threading
```

---

## 十六、实战教程：从零搭建完整项目

> 这一章手把手带你从零创建一个完整的 Python 项目，覆盖环境搭建、项目结构、代码编写、依赖管理、运行调试全流程。跟着做一遍就能上手。

### 16.1 第一步：安装 Python

**Windows 用户**

1. 打开 https://www.python.org/downloads/
2. 下载最新稳定版（推荐 3.12.x）
3. **安装时务必勾选 "Add Python to PATH"**（这一步最关键！）
4. 安装完成后打开 PowerShell 验证：

```bash
python --version
# 输出：Python 3.12.x

pip --version
# 输出：pip 24.x.x from ... (python 3.12)
```

> 如果提示 "python 不是内部命令"，说明安装时没勾选 Add to PATH。解决方法：重新运行安装程序 → 勾选 "Add to PATH" → 点 Modify → 完成。

### 16.2 第二步：创建项目目录

```bash
# 创建项目文件夹
mkdir my_first_project
cd my_first_project

# 用 VS Code 打开（推荐编辑器）
code .
```

### 16.3 第三步：创建虚拟环境

虚拟环境是 Python 项目的标配，它让每个项目拥有独立的依赖包，互不干扰。

```bash
# 创建虚拟环境（在项目根目录执行）
python -m venv .venv

# 激活虚拟环境
.venv\Scripts\activate       # Windows PowerShell
# source .venv/bin/activate  # Linux/Mac

# 激活后命令行前面会出现 (.venv)
# 此时 pip install 的包都装在这个虚拟环境里
```

**VS Code 配置**（重要！）

激活虚拟环境后，还需要让 VS Code 使用它：
1. `Ctrl + Shift + P` 打开命令面板
2. 输入 `Python: Select Interpreter`
3. 选择 `.venv` 中的 Python 解释器

这样 VS Code 的代码提示、终端运行都会使用这个虚拟环境。

### 16.4 第四步：搭建项目结构

一个规范的 Python 项目结构如下：

```
my_first_project/
├── .venv/                  # 虚拟环境（不要提交到 Git）
├── .gitignore              # Git 忽略规则
├── requirements.txt        # 依赖列表
├── README.md               # 项目说明
├── main.py                 # 程序入口
├── config.py               # 配置文件
├── models/                 # 数据模型
│   └── __init__.py
├── services/               # 业务逻辑
│   └── __init__.py
├── utils/                  # 工具函数
│   └── __init__.py
└── tests/                  # 测试代码
    └── __init__.py
```

**创建这些文件和目录：**

```bash
# Windows PowerShell
mkdir models, services, utils, tests
New-Item -ItemType File -Path "models/__init__.py" -Force
New-Item -ItemType File -Path "services/__init__.py" -Force
New-Item -ItemType File -Path "utils/__init__.py" -Force
New-Item -ItemType File -Path "tests/__init__.py" -Force
New-Item -ItemType File -Path "main.py" -Force
New-Item -ItemType File -Path "config.py" -Force
New-Item -ItemType File -Path "requirements.txt" -Force
New-Item -ItemType File -Path "README.md" -Force
New-Item -ItemType File -Path ".gitignore" -Force
```

> **`__init__.py` 是什么？** 它是一个空文件，告诉 Python "这个目录是一个包（package），可以被 import"。内容可以为空，但文件必须存在。

### 16.5 第五步：编写第一个程序

让我们写一个简单的**待办事项管理器**作为练手项目。

**`config.py` —— 配置文件**

```python
# 配置文件：集中管理项目配置
APP_NAME = "待办事项管理器"
VERSION = "1.0.0"
DATA_FILE = "todos.json"
```

**`models/__init__.py` —— 数据模型**

```python
"""数据模型：定义待办事项的数据结构"""
from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional


@dataclass
class Todo:
    """待办事项"""
    title: str
    id: int
    completed: bool = False
    priority: str = "普通"    # 高 / 普通 / 低
    created_at: str = field(default_factory=lambda: datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    description: Optional[str] = None

    def to_dict(self) -> dict:
        """转换为字典（用于 JSON 序列化）"""
        return {
            "id": self.id,
            "title": self.title,
            "completed": self.completed,
            "priority": self.priority,
            "created_at": self.created_at,
            "description": self.description,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "Todo":
        """从字典创建（用于 JSON 反序列化）"""
        return cls(**data)
```

**`services/__init__.py` —— 业务逻辑**

```python
"""业务逻辑：管理待办事项的增删改查"""
import json
from pathlib import Path
from typing import Optional
from models import Todo


class TodoService:
    """待办事项服务"""

    def __init__(self, data_file: str = "todos.json"):
        self.data_file = Path(data_file)
        self.todos: list[Todo] = []
        self._next_id = 1
        self.load()

    def load(self):
        """从文件加载数据"""
        if self.data_file.exists():
            with open(self.data_file, "r", encoding="utf-8") as f:
                data = json.load(f)
                self.todos = [Todo.from_dict(item) for item in data]
                self._next_id = max((t.id for t in self.todos), default=0) + 1

    def save(self):
        """保存数据到文件"""
        with open(self.data_file, "w", encoding="utf-8") as f:
            json.dump([t.to_dict() for t in self.todos], f, ensure_ascii=False, indent=2)

    def add(self, title: str, priority: str = "普通", description: str = None) -> Todo:
        """添加待办事项"""
        todo = Todo(title=title, id=self._next_id, priority=priority, description=description)
        self.todos.append(todo)
        self._next_id += 1
        self.save()
        return todo

    def delete(self, todo_id: int) -> bool:
        """删除待办事项"""
        for i, todo in enumerate(self.todos):
            if todo.id == todo_id:
                self.todos.pop(i)
                self.save()
                return True
        return False

    def complete(self, todo_id: int) -> bool:
        """标记为已完成"""
        for todo in self.todos:
            if todo.id == todo_id:
                todo.completed = True
                self.save()
                return True
        return False

    def list_all(self, show_completed: bool = True) -> list[Todo]:
        """列出所有待办事项"""
        if show_completed:
            return self.todos
        return [t for t in self.todos if not t.completed]

    def find(self, keyword: str) -> list[Todo]:
        """按关键词搜索"""
        return [t for t in self.todos if keyword in t.title]
```

**`utils/__init__.py` —— 工具函数**

```python
"""工具函数：终端界面美化"""

# 颜色代码（终端 ANSI 转义序列）
class Color:
    GREEN = "\033[92m"
    RED = "\033[91m"
    YELLOW = "\033[93m"
    BLUE = "\033[94m"
    GRAY = "\033[90m"
    BOLD = "\033[1m"
    RESET = "\033[0m"


def print_header(title: str):
    """打印标题"""
    width = 50
    print(f"\n{Color.BOLD}{'=' * width}")
    print(f"  {title}")
    print(f"{'=' * width}{Color.RESET}\n")


def print_todo(todo, index: int = 0):
    """格式化打印单条待办"""
    status = f"{Color.GREEN}✓{Color.RESET}" if todo.completed else f"{Color.RED}○{Color.RESET}"
    priority_color = {
        "高": Color.RED,
        "普通": Color.YELLOW,
        "低": Color.GRAY,
    }.get(todo.priority, Color.RESET)

    print(f"  {status} [{priority_color}{todo.priority}{Color.RESET}] "
          f"{Color.BOLD}#{todo.id}{Color.RESET} {todo.title}")
    if todo.description:
        print(f"    {Color.GRAY}└─ {todo.description}{Color.RESET}")


def print_todos(todos: list, title: str = "待办列表"):
    """打印待办列表"""
    print_header(title)
    if not todos:
        print(f"  {Color.GRAY}（空）{Color.RESET}")
        return
    for i, todo in enumerate(todos, 1):
        print_todo(todo, i)
    print(f"\n  共 {len(todos)} 条")
```

**`main.py` —— 程序入口**

```python
"""待办事项管理器 —— 程序入口"""
import sys
from config import APP_NAME, VERSION
from services import TodoService
from utils import print_header, print_todos, Color


def show_menu():
    """显示操作菜单"""
    print(f"\n{Color.BLUE}请选择操作：{Color.RESET}")
    print("  1. 查看所有待办")
    print("  2. 添加新待办")
    print("  3. 标记为完成")
    print("  4. 删除待办")
    print("  5. 搜索待办")
    print("  0. 退出程序")


def main():
    """主循环"""
    service = TodoService()
    print_header(f"{APP_NAME} v{VERSION}")

    while True:
        show_menu()
        choice = input(f"\n{Color.BOLD}请输入选项编号：{Color.RESET}").strip()

        if choice == "1":
            todos = service.list_all()
            print_todos(todos)

        elif choice == "2":
            title = input("待办标题：").strip()
            if not title:
                print(f"{Color.RED}标题不能为空！{Color.RESET}")
                continue
            priority = input("优先级（高/普通/低，默认普通）：").strip() or "普通"
            desc = input("描述（可选，回车跳过）：").strip() or None
            todo = service.add(title, priority, desc)
            print(f"{Color.GREEN}✓ 已添加：#{todo.id} {todo.title}{Color.RESET}")

        elif choice == "3":
            todo_id = int(input("待办编号：").strip())
            if service.complete(todo_id):
                print(f"{Color.GREEN}✓ 已完成！{Color.RESET}")
            else:
                print(f"{Color.RED}未找到编号 {todo_id} 的待办{Color.RESET}")

        elif choice == "4":
            todo_id = int(input("待办编号：").strip())
            if service.delete(todo_id):
                print(f"{Color.GREEN}✓ 已删除！{Color.RESET}")
            else:
                print(f"{Color.RED}未找到编号 {todo_id} 的待办{Color.RESET}")

        elif choice == "5":
            keyword = input("搜索关键词：").strip()
            results = service.find(keyword)
            print_todos(results, f"搜索结果：{keyword}")

        elif choice == "0":
            print(f"\n{Color.GREEN}再见！{Color.RESET}\n")
            sys.exit(0)

        else:
            print(f"{Color.RED}无效选项，请重新输入{Color.RESET}")


if __name__ == "__main__":
    main()
```

### 16.6 第六步：运行程序

```bash
# 确保虚拟环境已激活（命令行前面有 (.venv)）
# 在项目根目录执行：
python main.py
```

你会看到这样的界面：

```
==================================================
  待办事项管理器 v1.0.0
==================================================

请选择操作：
  1. 查看所有待办
  2. 添加新待办
  3. 标记为完成
  4. 删除待办
  5. 搜索待办
  0. 退出程序

请输入选项编号：
```

### 16.7 第七步：添加第三方依赖

让我们给项目加上 `rich` 库，让终端输出更漂亮。

```bash
# 安装 rich 库
pip install rich

# 导出依赖到 requirements.txt
pip freeze > requirements.txt

# 查看 requirements.txt 内容
cat requirements.txt
# rich==13.7.0
# markdown-it-py==3.0.0
# ...
```

**`requirements.txt` 的内容示例：**

```
rich==13.7.0
```

> **为什么需要 requirements.txt？** 别人拿到你的项目后，只需执行 `pip install -r requirements.txt` 就能安装所有依赖，不需要手动一个个装。

### 16.8 第八步：用 rich 美化输出

修改 `utils/__init__.py`，用 `rich` 替代手动 ANSI 颜色代码：

```python
"""工具函数：使用 rich 库美化终端输出"""
from rich.console import Console
from rich.table import Table
from rich.panel import Panel

console = Console()


def print_header(title: str):
    """打印漂亮的标题"""
    console.print(Panel(title, style="bold blue", expand=False))


def print_todos(todos: list, title: str = "待办列表"):
    """用表格打印待办列表"""
    print_header(title)

    if not todos:
        console.print("  [dim]（空）[/dim]")
        return

    table = Table(show_header=True, header_style="bold cyan")
    table.add_column("状态", width=4, justify="center")
    table.add_column("ID", width=5, justify="right")
    table.add_column("优先级", width=6)
    table.add_column("标题")
    table.add_column("描述", style="dim")

    for todo in todos:
        status = "[green]✓[/green]" if todo.completed else "[red]○[/red]"
        priority = {
            "高": "[red]高[/red]",
            "普通": "[yellow]普通[/yellow]",
            "低": "[dim]低[/dim]",
        }.get(todo.priority, todo.priority)
        table.add_row(status, str(todo.id), priority, todo.title, todo.description or "")

    console.print(table)
    console.print(f"\n  共 [bold]{len(todos)}[/bold] 条")
```

### 16.9 第九步：添加 `.gitignore`

```gitignore
# Python
__pycache__/
*.py[cod]
*.egg-info/
dist/
build/

# 虚拟环境
.venv/
venv/
env/

# IDE
.vscode/
.idea/
*.swp
*.swo

# 数据文件（不提交到 Git）
todos.json

# 环境变量
.env
```

> **为什么要 `.gitignore`？** 防止虚拟环境、缓存文件、敏感配置等被提交到 Git 仓库。

### 16.10 第十步：写 README

```markdown
# 待办事项管理器

一个基于终端的待办事项管理工具，支持增删改查、优先级标记、数据持久化。

## 功能特性

- ✅ 添加/删除/完成待办事项
- 🔍 关键词搜索
- 🎯 优先级标记（高/普通/低）
- 💾 数据自动保存到 JSON 文件
- 🎨 彩色终端输出（基于 rich）

## 快速开始

### 1. 安装依赖

```bash
python -m venv .venv
.venv\Scripts\activate   # Windows
pip install -r requirements.txt
```

### 2. 运行

```bash
python main.py
```

## 项目结构

```
my_first_project/
├── main.py          # 程序入口
├── config.py        # 配置文件
├── models/          # 数据模型
├── services/        # 业务逻辑
├── utils/           # 工具函数
└── tests/           # 测试代码
```
```

### 16.11 完整项目总结

经过以上步骤，你拥有了一个完整的 Python 项目：

```
my_first_project/
├── .venv/              # 虚拟环境
├── .gitignore          # Git 忽略规则
├── requirements.txt    # 依赖清单
├── README.md           # 项目说明
├── main.py             # 程序入口（用户交互界面）
├── config.py           # 配置管理
├── models/
│   └── __init__.py     # Todo 数据模型（dataclass）
├── services/
│   └── __init__.py     # TodoService 业务逻辑（增删改查 + JSON 持久化）
├── utils/
│   └── __init__.py     # 终端美化输出（rich）
└── tests/
    └── __init__.py     # 测试代码（待补充）
```

**这个项目用到了哪些知识点？**

| 知识点 | 用在哪里 |
|--------|----------|
| dataclass | Todo 数据模型定义 |
| 类型提示 | 函数参数和返回值标注 |
| JSON 文件读写 | 数据持久化 |
| pathlib | 文件路径操作 |
| 列表推导式 | 过滤未完成事项 |
| 类与方法 | TodoService 封装业务逻辑 |
| 异常处理 | 输入验证 |
| 第三方库 | rich 美化输出 |
| 虚拟环境 | 依赖隔离 |
| `__name__ == "__main__"` | 程序入口判断 |

### 16.12 下一步：给项目加测试

```python
# tests/test_services.py
import pytest
from pathlib import Path
from services import TodoService


@pytest.fixture
def service(tmp_path):
    """创建测试用的 TodoService（使用临时目录）"""
    data_file = tmp_path / "test_todos.json"
    return TodoService(data_file=str(data_file))


def test_add_todo(service):
    """测试添加待办"""
    todo = service.add("买牛奶", priority="高")
    assert todo.title == "买牛奶"
    assert todo.priority == "高"
    assert todo.id == 1
    assert not todo.completed


def test_complete_todo(service):
    """测试标记完成"""
    todo = service.add("写报告")
    assert service.complete(todo.id)
    assert service.todos[0].completed


def test_delete_todo(service):
    """测试删除"""
    todo = service.add("过期任务")
    assert service.delete(todo.id)
    assert len(service.todos) == 0


def test_find_todo(service):
    """测试搜索"""
    service.add("买牛奶")
    service.add("买面包")
    service.add("写报告")
    results = service.find("买")
    assert len(results) == 2


def test_persistence(tmp_path):
    """测试数据持久化"""
    data_file = tmp_path / "test.json"
    
    # 创建并添加数据
    service1 = TodoService(data_file=str(data_file))
    service1.add("持久化测试")
    
    # 重新加载
    service2 = TodoService(data_file=str(data_file))
    assert len(service2.todos) == 1
    assert service2.todos[0].title == "持久化测试"
```

**运行测试：**

```bash
pip install pytest
pytest tests/ -v
```

输出：

```
tests/test_services.py::test_add_todo PASSED
tests/test_services.py::test_complete_todo PASSED
tests/test_services.py::test_delete_todo PASSED
tests/test_services.py::test_find_todo PASSED
tests/test_services.py::test_persistence PASSED

5 passed in 0.12s
```

### 16.13 更多练手项目推荐

| 难度 | 项目 | 涉及知识点 |
|------|------|------------|
| ⭐ | 猜数字游戏 | 随机数、循环、条件判断、输入输出 |
| ⭐⭐ | 密码生成器 | 字符串操作、random 模块、命令行参数 |
| ⭐⭐ | 文件批量重命名 | pathlib、正则、os 操作 |
| ⭐⭐⭐ | 天气查询 CLI | requests、JSON API 调用、异常处理 |
| ⭐⭐⭐ | Markdown 转 HTML | 文件读写、字符串处理、正则 |
| ⭐⭐⭐⭐ | 个人记账本 | SQLite 数据库、dataclass、终端 UI |
| ⭐⭐⭐⭐ | 网页爬虫 | requests、BeautifulSoup、数据提取 |
| ⭐⭐⭐⭐⭐ | REST API 服务 | FastAPI、Pydantic、数据库 CRUD |
| ⭐⭐⭐⭐⭐ | 桌面计算器 | PyQt6/Tkinter、事件驱动、GUI 布局 |

---

> 本知识库持续更新，覆盖 Python 生态核心内容。建议按路线图循序渐进，结合实战项目加深理解。每个知识点都动手敲一遍，比看十遍教程都管用。
