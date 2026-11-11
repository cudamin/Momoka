namespace Momoka.FnNas;

/// <summary>已确认移出的一次票，以及扣除本次身份已确认版本后是否已经没有剩余文件。</summary>
public sealed record FnNasRemovalReceipt(object Ticket, bool SubjectRemoved);
