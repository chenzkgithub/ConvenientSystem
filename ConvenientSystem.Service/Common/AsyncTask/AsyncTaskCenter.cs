using ConvenientSystem.Shared.Common;
using System.Collections.Concurrent;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>异步任务进度推送钩子：云端实现走 SignalR 定向推给任务发起人，桌面本地进程无 Hub 用空实现（前端轮询兜底）。</summary>
    public interface IAsyncTaskNotifier
    {
        void Notify(Guid userId, AsyncTaskDto snapshot);
    }

    /// <summary>空推送：桌面端本地任务没有 SignalR Hub，前端通过轮询获取进度。</summary>
    public sealed class NoopAsyncTaskNotifier : IAsyncTaskNotifier
    {
        public void Notify(Guid userId, AsyncTaskDto snapshot) { }
    }

    /// <summary>
    /// 异步任务中心：全部耗时操作（扫描/解析生成/Apifox 导入删除等）的统一进度表，Singleton 注册，
    /// 云端与桌面各自进程内一份。任务存内存（服务重启即失联，前端会标记失败提示人工核对），
    /// 完成结果保留 30 分钟；运行超过 2 小时的任务读取时惰性置为失败（防卡死任务永久占用并发额度）。
    /// 进度更新每任务独立锁线程安全；变化经 IAsyncTaskNotifier 节流推送（800ms，终态必推）。
    /// 快照通道（轮询/推送/恢复）一律剥离 Result 只带终态摘要 Summary，页面终态后经 GetResult 按需取完整结果。
    /// </summary>
    public sealed class AsyncTaskCenter
    {
        /// <summary>已完成任务的保留时长，超时后在下一次 Begin 时清扫。</summary>
        private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

        /// <summary>运行时长保险丝：超过后读取时惰性置失败。若任务其后真实完成，业务侧的状态写入会覆盖回最终结果。</summary>
        private static readonly TimeSpan MaxRunning = TimeSpan.FromHours(2);

        /// <summary>进度推送节流间隔：运行中最多 800ms 一条，避免高频进度刷爆推送。</summary>
        private static readonly TimeSpan PushThrottle = TimeSpan.FromMilliseconds(800);

        private readonly IAsyncTaskNotifier _notifier;

        /// <summary>任务表：键为任务 ID。服务为 Singleton，天然跨请求共享。</summary>
        private readonly ConcurrentDictionary<Guid, TaskState> _tasks = new();

        public AsyncTaskCenter(IAsyncTaskNotifier notifier)
        {
            _notifier = notifier;
        }

        /// <summary>注册新任务：同用户同类型已有进行中任务时拒绝（进度面板按类型区分，跨类型可并行）。</summary>
        public AsyncTaskHandle Begin(Guid? userId, string kind, string title)
        {
            lock (_tasks)
            {
                SweepExpired();
                foreach (var existing in _tasks.Values)
                    lock (existing.Lock)
                    {
                        // 超保险丝的旧任务读取即置失败：请求超时导致前端丢失任务状态时，僵死任务不永久阻塞同类型新任务
                        FailStale(existing);
                        if (existing.Dto.Status == "running" && existing.OwnerUserId == userId
                            && existing.Dto.Kind == kind)
                            // 409 + code 供前端识别「同类型任务进行中」，提供强制终止并重试的入口
                            throw new BizException($"已有进行中的「{existing.Dto.Title}」任务，请等待完成后再发起新任务",
                                    StatusCodes.Status409Conflict)
                                { Extras = new Dictionary<string, object?> { ["code"] = "task-running" } };
                    }

                var state = new TaskState(userId, kind, title);
                _tasks[state.Dto.TaskId] = state;
                // 创建即推送初始快照：启动响应因超时丢失时，发起人面板仍能收到任务（含 taskId）并自动开始轮询；
                // 桌面本地任务无归属用户，Notify 内部直接跳过
                Notify(state, true);
                return new AsyncTaskHandle(state);
            }
        }

        /// <summary>强制终止指定用户指定类型的进行中任务（请求超时导致前端丢失任务状态时解锁重发）；
        /// 终止成功立即推送终态（面板同步置失败）。返回是否终止了任务（无进行中任务返回 false）。</summary>
        public bool CancelRunning(Guid? userId, string kind)
        {
            lock (_tasks)
            {
                foreach (var state in _tasks.Values)
                    lock (state.Lock)
                        if (state.Dto.Status == "running" && state.OwnerUserId == userId && state.Dto.Kind == kind)
                        {
                            state.Dto.Status = "failed";
                            state.Dto.Error = "已被用户强制终止（上次请求可能超时，实际执行结果请人工核对）";
                            state.Dto.Current = "";
                            state.Dto.FinishedAt = TimeHelper.Now;
                            Notify(state, true);
                            return true;
                        }
                return false;
            }
        }

        /// <summary>查询任务进度轻量快照（不含 Result）；任务不存在、已过期（完成超 30 分钟）或非本人任务返回 null。</summary>
        public AsyncTaskDto? GetTask(Guid taskId, Guid? userId)
        {
            if (!_tasks.TryGetValue(taskId, out var state)) return null;
            lock (state.Lock)
            {
                if (state.OwnerUserId != userId) return null;
                FailStale(state);
                return Snapshot(state.Dto);
            }
        }

        /// <summary>读取已完成任务的完整 Result（大负载按需拉取）：任务不存在、未完成、已过期或非本人任务返回 null。
        /// 进度轮询/推送/恢复只发轻量快照，页面在任务终态后经此取回完整结果。</summary>
        public AsyncTaskDto? GetResult(Guid taskId, Guid? userId)
        {
            if (!_tasks.TryGetValue(taskId, out var state)) return null;
            lock (state.Lock)
            {
                if (state.OwnerUserId != userId) return null;
                FailStale(state);
                // 运行中（且保险丝未触发）无结果可取；FailStale 只会 running→failed，不会反向
                return state.Dto.Status == "running" ? null : Snapshot(state.Dto, true);
            }
        }

        /// <summary>指定用户的全部未清理任务（含超时保险丝判定），按开始时间倒序；任务面板刷新恢复用。桌面本地任务无登录用户（userId=null）。</summary>
        public List<AsyncTaskDto> GetTasks(Guid? userId)
        {
            var result = new List<AsyncTaskDto>();
            foreach (var state in _tasks.Values)
            {
                lock (state.Lock)
                {
                    if (state.OwnerUserId != userId) continue;
                    FailStale(state);
                    result.Add(Snapshot(state.Dto));
                }
            }
            return result.OrderByDescending(t => t.StartedAt).ToList();
        }

        /// <summary>在任务锁内读取进度与业务负载（业务侧需要一致快照时使用，如 ReExport 取 Document+SelectionKeys）。</summary>
        /// <returns>reader 的返回值；任务不存在/非本人返回 default。</returns>
        public T? Read<T>(Guid taskId, Guid? userId, Func<AsyncTaskDto, T> reader)
        {
            if (!_tasks.TryGetValue(taskId, out var state)) return default;
            lock (state.Lock)
            {
                if (state.OwnerUserId != userId) return default;
                FailStale(state);
                return reader(state.Dto);
            }
        }
        /// <summary>更新任务进度（线程安全）：lambda 在任务锁内执行；终态变更立即推送，运行中按节流推送。</summary>
        public void Update(AsyncTaskHandle handle, Action<AsyncTaskDto> update)
        {
            var state = handle.State;
            bool final;
            lock (state.Lock)
            {
                update(state.Dto);
                final = state.Dto.Status != "running";
            }
            Notify(state, final);
        }

        /// <summary>任务失败（业务 catch 分支调用）：置失败态并立即推送。</summary>
        public void Fail(AsyncTaskHandle handle, string error)
            => Update(handle, p =>
            {
                p.Status = "failed";
                p.Current = "";
                p.Error = error;
            });

        /// <summary>任务收尾（业务 finally 分支调用）：记录结束时间并强制推送最终状态（幂等，可重入）。</summary>
        public void Finish(AsyncTaskHandle handle)
        {
            var state = handle.State;
            lock (state.Lock)
                state.Dto.FinishedAt ??= TimeHelper.Now;
            Notify(state, true);
        }

        /// <summary>推送进度给任务发起人：终态必推，运行中按节流间隔推。</summary>
        private void Notify(TaskState state, bool force)
        {
            if (state.OwnerUserId is not { } userId || userId == Guid.Empty) return;
            AsyncTaskDto snapshot;
            lock (state.Lock)
            {
                var now = TimeHelper.Now;
                if (!force && state.Dto.Status == "running" && now - state.LastPushAt < PushThrottle) return;
                state.LastPushAt = now;
                snapshot = Snapshot(state.Dto);
            }
            _notifier.Notify(userId, snapshot);
        }

        /// <summary>清扫已过保留期的完成任务（调用方持有 _tasks 锁）。</summary>
        private void SweepExpired()
        {
            var cutoff = TimeHelper.Now - Retention;
            foreach (var stale in _tasks.Where(kv => kv.Value.Dto.FinishedAt is { } finished && finished < cutoff)
                         .Select(kv => kv.Key).ToList())
                _tasks.TryRemove(stale, out _);
        }

        /// <summary>惰性超时保险丝：运行超过保险丝时长的任务读取时置为失败，释放并发额度（调用方持有任务锁）。</summary>
        private static void FailStale(TaskState state)
        {
            if (state.Dto.Status == "running" && TimeHelper.Now - state.Dto.StartedAt > MaxRunning)
            {
                state.Dto.Status = "failed";
                state.Dto.Error = "任务运行超时（超过 2 小时未结束），可能已中断，请人工核对实际执行结果";
                state.Dto.FinishedAt = TimeHelper.Now;
            }
        }

        /// <summary>进度快照：默认剥离 Result（生成任务的 Result 可达数 MB，轮询/推送/恢复通道只发进度与终态摘要），
        /// includeResult 仅供 GetResult 端点使用。Result/Payload 赋值后不再修改，浅拷贝引用安全。</summary>
        private static AsyncTaskDto Snapshot(AsyncTaskDto source, bool includeResult = false) => new()
        {
            TaskId = source.TaskId,
            Kind = source.Kind,
            Title = source.Title,
            Status = source.Status,
            Phase = source.Phase,
            Current = source.Current,
            Total = source.Total,
            Completed = source.Completed,
            Failed = source.Failed,
            Error = source.Error,
            Summary = source.Summary,
            StartedAt = source.StartedAt,
            FinishedAt = source.FinishedAt,
            Result = includeResult ? source.Result : null,
        };

        /// <summary>任务句柄：业务侧持有并用于更新进度与收尾，不直接暴露内部状态（State 仅供任务中心内部使用）。</summary>
        public sealed class AsyncTaskHandle
        {
            // internal：与 TaskState 的 internal 保持一致——internal 成员暴露 private 类型触发 CS0051，
            // private 成员包含类又访问不到（Begin/Update/Finish 触发 CS0122）；TaskState 仅本程序集可见
            internal TaskState State { get; }

            internal AsyncTaskHandle(TaskState state) => State = state;

            public Guid TaskId => State.Dto.TaskId;
        }

        /// <summary>任务运行态：internal 嵌套（仅本程序集可见），与 AsyncTaskHandle 成员可访问性一致。</summary>
        internal sealed class TaskState
        {
            public AsyncTaskDto Dto { get; } = new();
            public Guid? OwnerUserId { get; }
            public DateTime LastPushAt { get; set; } = DateTime.MinValue;
            public object Lock { get; } = new();

            public TaskState(Guid? ownerUserId, string kind, string title)
            {
                OwnerUserId = ownerUserId;
                Dto.TaskId = Guid.NewGuid();
                Dto.Kind = kind;
                Dto.Title = title;
                Dto.Status = "running";
                Dto.Current = "正在准备...";
                Dto.StartedAt = TimeHelper.Now;
            }
        }
    }
}