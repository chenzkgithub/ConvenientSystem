/**
 * Git 命令知识库数据：常用命令速查（分类 + 说明 + 危险标记）。
 * 每条命令都可复制，也可对当前选中仓库直接执行（走后端白名单校验）。
 */

export interface GitCommandEntry {
  /** 分类 */
  category: string
  /** 命令（占位符用尖括号，如 <branch>） */
  command: string
  /** 一句话说明 */
  desc: string
  /** 危险命令（改写历史/不可逆），列表红色警示、执行前确认 */
  danger?: boolean
}

export const GIT_CATEGORY_ALL = '全部'

export const gitCategories: string[] = [
  GIT_CATEGORY_ALL,
  '基础操作',
  '分支管理',
  '远程协作',
  '撤销恢复',
  '储藏暂存',
  '标签管理',
  '子模块',
  '问题排查',
]

export const gitCommands: GitCommandEntry[] = [
  // ============================ 基础操作 ============================
  { category: '基础操作', command: 'git init', desc: '在当前目录初始化一个新 Git 仓库' },
  { category: '基础操作', command: 'git clone <url>', desc: '克隆远程仓库到本地' },
  { category: '基础操作', command: 'git status', desc: '查看工作区与暂存区状态' },
  { category: '基础操作', command: 'git status -s', desc: '紧凑模式查看状态（每行一个文件）' },
  { category: '基础操作', command: 'git add .', desc: '暂存当前目录全部改动（含新增文件）' },
  { category: '基础操作', command: 'git add -p', desc: '交互式逐块选择暂存（精细控制提交内容）' },
  { category: '基础操作', command: 'git commit -m "<msg>"', desc: '提交暂存区改动并写说明' },
  { category: '基础操作', command: 'git commit -a -m "<msg>"', desc: '跳过 add 直接提交已跟踪文件的改动' },
  { category: '基础操作', command: 'git commit --amend', desc: '修正最近一次提交（补文件/改说明，未推送时安全）', danger: true },
  { category: '基础操作', command: 'git log --oneline -10', desc: '最近 10 条提交，单行紧凑显示' },
  { category: '基础操作', command: 'git log --graph --oneline --all', desc: '图形化查看全部分支的提交拓扑' },
  { category: '基础操作', command: 'git diff', desc: '查看工作区未暂存的改动' },
  { category: '基础操作', command: 'git diff --staged', desc: '查看已暂存待提交的改动' },

  // ============================ 分支管理 ============================
  { category: '分支管理', command: 'git branch', desc: '列出本地分支（* 标记当前分支）' },
  { category: '分支管理', command: 'git branch -a', desc: '列出全部分支（本地 + 远程）' },
  { category: '分支管理', command: 'git branch -vv', desc: '查看分支跟踪关系与领先/落后详情' },
  { category: '分支管理', command: 'git switch <branch>', desc: '切换到已有分支' },
  { category: '分支管理', command: 'git switch -c <new-branch>', desc: '新建分支并立即切换过去' },
  { category: '分支管理', command: 'git merge <branch>', desc: '把指定分支合并到当前分支' },
  { category: '分支管理', command: 'git merge --abort', desc: '合并冲突时中止合并，回到合并前状态' },
  { category: '分支管理', command: 'git branch -m <old> <new>', desc: '重命名分支' },
  { category: '分支管理', command: 'git branch -d <branch>', desc: '删除已合并的本地分支' },
  { category: '分支管理', command: 'git branch -D <branch>', desc: '强制删除本地分支（未合并提交会丢失）', danger: true },
  { category: '分支管理', command: 'git cherry-pick <hash>', desc: '把其它分支的指定提交摘到当前分支' },

  // ============================ 远程协作 ============================
  { category: '远程协作', command: 'git remote -v', desc: '查看远程仓库地址列表' },
  { category: '远程协作', command: 'git fetch --all', desc: '拉取全部远程更新（不合并到本地分支）' },
  { category: '远程协作', command: 'git pull', desc: '拉取远程更新并合并到当前分支' },
  { category: '远程协作', command: 'git pull --rebase', desc: '变基式拉取：本地提交放远程之后，历史线性整洁' },
  { category: '远程协作', command: 'git push', desc: '推送当前分支到远程' },
  { category: '远程协作', command: 'git push -u origin <branch>', desc: '首次推送并建立跟踪关系，之后可直接 git push' },
  { category: '远程协作', command: 'git push --force-with-lease', desc: '安全强推：远程被他人更新过则拒绝（优于 --force）', danger: true },
  { category: '远程协作', command: 'git remote set-url origin <url>', desc: '修改远程仓库地址（如迁移服务器后）' },
  { category: '远程协作', command: 'git remote prune origin', desc: '清理本地缓存的、远程已删除的分支引用' },
  { category: '远程协作', command: 'git branch -r', desc: '只看远程分支列表' },
  { category: '远程协作', command: 'git branch --set-upstream-to=origin/<branch>', desc: '为当前分支手动建立远程跟踪关系' },

  // ============================ 撤销恢复 ============================
  { category: '撤销恢复', command: 'git restore <file>', desc: '丢弃单个文件的工作区改动（不可恢复）', danger: true },
  { category: '撤销恢复', command: 'git restore --staged <file>', desc: '把文件移出暂存区（改动保留在工作区）' },
  { category: '撤销恢复', command: 'git restore --source=<hash> <file>', desc: '检出指定历史版本的文件' },
  { category: '撤销恢复', command: 'git reset --soft HEAD~1', desc: '撤销最近提交，改动回到暂存区' },
  { category: '撤销恢复', command: 'git reset --mixed HEAD~1', desc: '撤销最近提交，改动回到工作区（默认模式）' },
  { category: '撤销恢复', command: 'git reset --hard HEAD~1', desc: '撤销最近提交并丢弃全部改动（不可恢复）', danger: true },
  { category: '撤销恢复', command: 'git revert <hash>', desc: '生成一笔反向提交来撤销指定提交（不改历史，安全）' },
  { category: '撤销恢复', command: 'git clean -fd', desc: '删除全部未跟踪文件和目录（不可恢复）', danger: true },
  { category: '撤销恢复', command: 'git checkout -- .', desc: '丢弃全部工作区改动（旧语法，不可恢复）', danger: true },
  { category: '撤销恢复', command: 'git reflog', desc: '查看本地引用日志，找回“丢失”的提交（后悔药）' },

  // ============================ 储藏暂存 ============================
  { category: '储藏暂存', command: 'git stash', desc: '把当前改动暂存起来，工作区恢复干净' },
  { category: '储藏暂存', command: 'git stash -u', desc: '暂存改动时包含未跟踪的新文件' },
  { category: '储藏暂存', command: 'git stash push -m "<msg>"', desc: '带说明暂存，方便识别用途' },
  { category: '储藏暂存', command: 'git stash list', desc: '查看储藏列表' },
  { category: '储藏暂存', command: 'git stash pop', desc: '恢复最近一次储藏并从列表删除' },
  { category: '储藏暂存', command: 'git stash apply', desc: '恢复最近一次储藏但保留副本（多处复用时）' },
  { category: '储藏暂存', command: 'git stash drop', desc: '删除最近一次储藏' },
  { category: '储藏暂存', command: 'git stash clear', desc: '清空全部储藏（不可恢复）', danger: true },
  { category: '储藏暂存', command: 'git stash branch <new-branch>', desc: '基于储藏内容新建分支并恢复' },

  // ============================ 标签管理 ============================
  { category: '标签管理', command: 'git tag', desc: '列出全部标签' },
  { category: '标签管理', command: 'git tag -l "v1.*"', desc: '按通配符过滤标签' },
  { category: '标签管理', command: 'git tag <name>', desc: '在当前提交打轻量标签' },
  { category: '标签管理', command: 'git tag -a <name> -m "<msg>"', desc: '打附注标签（含说明、作者，推荐发布用）' },
  { category: '标签管理', command: 'git show <tag>', desc: '查看标签指向的提交详情' },
  { category: '标签管理', command: 'git tag -d <name>', desc: '删除本地标签' },
  { category: '标签管理', command: 'git push origin <tag>', desc: '推送单个标签到远程' },
  { category: '标签管理', command: 'git push origin --tags', desc: '推送全部本地标签到远程' },

  // ============================ 子模块 ============================
  { category: '子模块', command: 'git submodule status', desc: '查看子模块状态（当前指向的提交）' },
  { category: '子模块', command: 'git submodule add <url> <path>', desc: '添加子模块（把外部仓库嵌进当前仓库）' },
  { category: '子模块', command: 'git submodule init', desc: '初始化 .gitmodules 里登记的子模块' },
  { category: '子模块', command: 'git submodule update --init --recursive', desc: '克隆并检出子模块（含嵌套，新环境必跑）' },
  { category: '子模块', command: 'git submodule update --remote', desc: '把子模块更新到其远程的最新提交' },
  { category: '子模块', command: 'git submodule sync', desc: '同步子模块的远程地址（主仓库改址后）' },
  { category: '子模块', command: 'git submodule deinit <path>', desc: '停用子模块（保留配置，重新 init 可恢复）' },

  // ============================ 问题排查 ============================
  { category: '问题排查', command: 'git blame <file>', desc: '逐行追溯文件每行的最后修改者和提交' },
  { category: '问题排查', command: 'git log -p <file>', desc: '查看单个文件的完整提交历史与每次改动' },
  { category: '问题排查', command: 'git log -S "<text>"', desc: '搜索增加/删除了指定文本的提交（找某行代码来历）' },
  { category: '问题排查', command: 'git log --since="1 week ago" --oneline', desc: '最近一周的提交记录' },
  { category: '问题排查', command: 'git show <hash>', desc: '查看指定提交的完整改动内容' },
  { category: '问题排查', command: 'git show --stat HEAD', desc: '最近一次提交的文件改动统计' },
  { category: '问题排查', command: 'git shortlog -sn', desc: '按作者统计提交数量排行' },
  { category: '问题排查', command: 'git diff --stat HEAD~1', desc: '与上一提交的文件级差异统计' },
  { category: '问题排查', command: 'git bisect start', desc: '二分法定位引入问题的提交（配合 good/bad 标记）' },
  { category: '问题排查', command: 'git fsck --lost-found', desc: '检查仓库对象完整性，列出悬空对象（找回丢失提交）' },
  { category: '问题排查', command: 'git config -l', desc: '查看当前生效的全部 Git 配置' },
]
