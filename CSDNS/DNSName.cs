using System.Text;

namespace CSDNS;

internal enum State
{
    LabelBegin, // 域名中一个标签开始
    LabelIn, // 在一个标签处理过程中
    LabelEnd, //域名中一个标签结束

    // 解析转义字符后面的数字, 一定解析后面的三个字符, 如果不到三个字符则抛出异常, 到了三个字符就进入到上面的三个状
    BeginEscapeNumber,
    InEscapeNumber, // 进入对转移字符转义的状态
    EndEscapeNumber, // 对数字进行转义
}

internal class ByteComparer : IEqualityComparer<Byte>
{
    private static Byte ToLower(Byte b) => (Byte)Char.ToLower((Char)b);

    public bool Equals(Byte x, Byte y) => ToLower(x) == ToLower(y);

    public int GetHashCode(Byte obj) => ToLower(obj).GetHashCode();
}

public class DNSName
{
    /// <summary>
    ///  定义一些程序用到的常量:
    ///   * `MaxDNSNameLength` 表示一整个 DNSName 的长度不应该超过 255 个字节;
    ///   * `MaxLabelLength` 表示, DNSName 中的一个标签不应该超过 63 个字节
    /// </summary>
    private const UInt16 MaxDNSNameLength = 255, MaxLabelLength = 63;

    private const Byte
        PointByte = (Byte)'.',
        EndByte = (Byte)'\0',
        // 这个 Backslash 是字符名称, 但是不少项目使用的状态名称含有 `Escape`, 其实指的就是 `Escape Characters` 转义字符
        BackslashByte = (Byte)'\\';

    private Byte[] name;
    private Int32[] offsets; // 这个变量参考项目中没有我为了实现简单自己定义了一个


    public DNSName()
    {
        name = [];
        offsets = [];
    }

    public DNSName(DNSName rhs)
    {
        name = rhs.name;
        offsets = rhs.offsets;
    }

    // 从一个字符串解析为 dns
    public DNSName(String s) : this(Encoding.UTF8.GetBytes(s)) { }

    public DNSName(Byte[] bytes)
    {
        if (bytes.Length == 0 || bytes is [PointByte])
        {
            name = [EndByte];
            offsets = [0];
        }
        else
        {
            var output = ResolveDNSName(bytes);
            name = output.Item1;
            offsets = output.Item2;
        }
    }

    /// <summary>
    /// 把 一个 Byte 数组中的 DNSName 转换为对人类友好的字符串
    /// 是 `std::string DNSName::toString` 和 `void DNSName::toString` 的结合体
    /// </summary>
    /// <param name="separator">label之间的分割符号</param>
    /// <param name="trailing">是否在解析完成后保留解析字符串末尾的最后一个分隔符</param>
    /// <returns></returns>
    /// <exception cref="IndexOutOfRangeException"></exception>
    public String ToString(String separator = ".", Boolean trailing = true)
    {
        if (Empty())
            throw new IndexOutOfRangeException("尝试对一个没有设定值的 DNSName 进行打印操作");
        if (IsRoot())
            return trailing ? separator : "";
        var stringBuilder = new StringBuilder();
        foreach (var offset in offsets)
        {
            var labelLength = name[offset];
            ToLabel(stringBuilder, offset + 1, labelLength);
            stringBuilder.Append(separator);
        }

        if (!trailing)
            stringBuilder.Length -= separator.Length;

        return stringBuilder.ToString();
    }

    /// <summary>
    /// 获取一个 DNSName 的所有标签; 使用 Bute[] 进行存储, 而不是像原来项目一样使用字符串进行存储
    /// </summary>
    public List<Byte[]> GetRawLabels()
    {
        var ret = new List<Byte[]>(offsets.Length);
        foreach (var offset in offsets)
        {
            var labelLength = name[offset];
            if (labelLength == 0)
                break;
            var label = new Byte[labelLength];
            Array.Copy(name, offset + 1, label, 0, labelLength);
            ret.Add(label);
        }

        return ret;
    }

    public Boolean Include(DNSName other) => IsInclude(other);

    /// <summary>
    /// 判断当前域名是否含有另一个域名; 使用这个方法替换了参考项目里面的 `isPartOf` 因为我觉得这个写的不是很好
    /// TODO: 感觉这个函数的命名不是很合适, 希望以后能改进
    /// </summary>
    /// <param name="other">另外一个域名</param>
    /// <returns></returns>
    public Boolean IsInclude(DNSName other)
    {
        // 这些 if 条件判断, 有的是用来加速判断, 有的是真有用
        // 自己一定包含自己 
        if (this == other) return true;
        // 一定包含空和 Root (包含 root 的前提是自己非空)
        if (other.Empty() || (!Empty() && other.IsRoot())) return true;
        // 长的不可能包含短的 (包括 label 的个数)
        if (name.Length < other.name.Length || offsets.Length < other.offsets.Length) return false;
        for (var i = 1; i <= other.offsets.Length; i++)
        {
            // t 和 o 分别表示 this 和 other
            Int32 tLabelLength = name[offsets[^i]], oLabelLength = other.name[other.offsets[^i]];
            if (tLabelLength != oLabelLength) return false;
            for (var j = 1; j <= oLabelLength; j++)
                if (name[offsets[^i] + j] != other.name[other.offsets[^i] + j])
                    return false;
        }

        return true;
    }

    public static Boolean operator ==(DNSName lhs, DNSName rhs)
    {
        // 如果两者指向同一个对象, 则说明两者相等
        if (ReferenceEquals(lhs, rhs)) return true;
        // 如果两个 DNSName 的 label 个数以及 name 的长度都不相等,那一定不相等
        if (lhs.offsets.Length != rhs.offsets.Length || lhs.name.Length != rhs.name.Length) return false;
        // 最后按顺序比较 name (这里默认如果 name 一致, 那么 offsets 也就一致了)
        return lhs.name.SequenceEqual(rhs.name, new ByteComparer());
    }

    public static Boolean operator !=(DNSName lhs, DNSName rhs) => !(lhs == rhs);

    public Boolean Empty() => name.Length == 0;

    public DNSName MakeRelative(DNSName zone)
    {
        if (IsInclude(zone))
        {
            name = name.Take(name.Length - zone.name.Length).Append<Byte>(0).ToArray();
            offsets = offsets.Take(offsets.Length - zone.offsets.Length).ToArray();
        }
        else
            throw new NotImplementedException("暂未实现: 如果不包含在内的处理方式, 因为我觉得参考项目中的实现方式不是很合理");

        return this;
    }


    // TODO: 这个函数的命名不是很合适, 应该是动词在前名词在后 (对函数来说如此)
    public DNSName LabelReverse()
    {
        var ret = new DNSName();
        if (IsRoot()) return this;
        if (Empty()) return ret;
        var labels = GetRawLabels();
        labels.Reverse();
        ret.AppendRawLabels(labels);
        return ret;
    }


    // TODO: `PrependRawLabel(String label)` 和 `AppendRawLabel(String label)` 函数都一个很大的问题就是
    //       不对字符串中存在的转义字符进行转义, 不知道这一点需不需要做

    public DNSName PrependRawLabel(String label) => PrependRawLabel(Encoding.UTF8.GetBytes(label));

    public DNSName PrependRawLabel(Byte[] label)
    {
        CheckLabelLength(label.Length);
        if (name.Length + label.Length > MaxDNSNameLength - 1)
            throw new IndexOutOfRangeException(
                $"新增的 dns name 长度 {name.Length + label.Length} 大于 DNSName 最大长度 {MaxDNSNameLength}");
        var nameList = name.ToList();
        var offsetsList = offsets.ToList();
        if (Empty())
        {
            nameList.Add((Byte)label.Length);
            nameList.AddRange(label);
            nameList.Add(0);
            name = nameList.ToArray();
            offsets = [0];
        }
        else
        {
            nameList.Insert(0, (Byte)label.Length);
            nameList.InsertRange(1, label);
            var addLength = label.Length + 1;
            for (var i = 1; i < offsetsList.Count; i++)
                offsetsList[i] += addLength;
            offsetsList.Insert(1, addLength);
            name = nameList.ToArray();
            offsets = offsetsList.ToArray();
        }

        return this;
    }

    private static void CheckLabelLength(Int32 length)
    {
        switch (length)
        {
            case 0:
                throw new IndexOutOfRangeException("no such thing as an empty label to append");
            case > MaxLabelLength:
                throw new IndexOutOfRangeException($"label length {length} exceeds maximum of {MaxLabelLength}");
        }
    }

    /// <summary>
    /// 针对追加多个标签的情景进行了专属的优化, 省去了不少检查以及 List 和 Array 转化所造成的性能损失 (没测试过字节感觉性能损失不小)
    /// </summary>
    /// <param name="labels"></param>
    /// <exception cref="IndexOutOfRangeException"></exception>
    private void AppendRawLabels(List<Byte[]> labels)
    {
        var nameList = name.ToList();
        var offsetsList = offsets.ToList();
        if (!Empty()) nameList.RemoveAt(nameList.Count - 1); // 如果不为空记得删除末尾的 0 
        foreach (var label in labels)
        {
            CheckLabelLength(label.Length);
            if (nameList.Count + label.Length > MaxDNSNameLength - 1)
                throw new IndexOutOfRangeException(
                    $"新增的 dns name 长度 {nameList.Count + label.Length} 大于 DNSName 最大长度 {MaxDNSNameLength}");
            offsetsList.Add(nameList.Count);
            nameList.Add((Byte)label.Length);
            nameList.AddRange(label);
        }

        nameList.Add(0);
        name = nameList.ToArray();
        offsets = offsetsList.ToArray();
    }

    public DNSName AppendRawLabel(String label) => AppendRawLabel(Encoding.UTF8.GetBytes(label));

    public DNSName AppendRawLabel(Byte[] label)
    {
        CheckLabelLength(label.Length);

        if (name.Length + label.Length > MaxDNSNameLength - 1)
            throw new IndexOutOfRangeException(
                $"新增的 dns name 长度 {name.Length + label.Length} 大于 DNSName 最大长度 {MaxDNSNameLength}");
        // 这里使用 List 来对 name 和 offsets 进行拼接操作 (也许不是最高效的方式?)
        // 参考项目中有根据 label 的长度预先分配空间的代码, 但是 C# 并没有这么精细级别的操作, 所以没有把那部分代码抄过来
        // 考虑到我是使用数组进行存储的, 这里我们假定数组是不可变的, 所以这里使用 List 进行拼接
        var nameList = name.ToList();
        var offsetsList = offsets.ToList();
        if (!Empty()) nameList.RemoveAt(nameList.Count - 1);
        offsetsList.Add(nameList.Count);
        nameList.Add((Byte)label.Length);
        nameList.AddRange(label);
        nameList.Add(0);
        name = nameList.ToArray();
        offsets = offsetsList.ToArray();
        return this;
    }
    
    public Int32 GetLabelCount() => offsets.Length;

    public Int32 CountLabels() => offsets.Length; // TODO: 这个函数纯纯是为了和参考项目中函数名对应才写的, 以后有机会删除它
    
    private Boolean IsDigit(Byte b) => b is >= (Byte)'0' and <= (Byte)'9';

    private Boolean IsRoot() => name[0] == EndByte;

    /// <summary>
    ///  将一个 Byte 数组中的 DNSName 标签转换为字符串. 等价于参考项目中的 `appendEscapedLabel` 函数
    /// </summary>
    /// 
    /// <param name="stringBuilder">调用函数中新建的字符串拼接器</param>
    /// <param name="start"><code>name</code>中开始解析的位置</param>
    /// <param name="length"><code>name</code>中需要解析的长度</param>
    /// <exception cref="InvalidOperationException"></exception>
    private void ToLabel(StringBuilder stringBuilder, Int32 start, Int32 length)
    {
        var i = 0;
        while (i < length)
        {
            var b = name[start + i];
            if (b == PointByte) // 如果域名内本身就含有 '.' 这和符号, 那么就需要转义输出
                stringBuilder.Append("\\.");
            else if (b == BackslashByte)
                stringBuilder.Append(@"\\"); // 这个 @"\\" 等价于 "\\\\"
            else if (b is > 0x20 and < 0x7F) // 可显示的 ASCII 可打印字符集合
                stringBuilder.Append((char)b);
            else
            {
                var buffer = b.ToString("D3");
                if (buffer.Length > 3)
                    throw new InvalidOperationException("1");
                stringBuilder.Append('\\').Append(buffer);
            }

            ++i;
        }
    }

    /// <summary>
    ///  解析 DNSName, 并且返回一个 Byte 数组, 这个数组中包含一个 DNSName 的所有字节, 包括尾随的零
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns>解析后的 DNSName</returns>
    /// <example>
    /// www.bupt.edu.cn -> 3www4bupt3edu2cn0
    /// offsets -> [0, 4, 9, 13]
    /// </example>
    /// <exception cref="FormatException"></exception>
    private Tuple<Byte[], Int32[]> ResolveDNSName(Byte[] bytes)
    {
        List<Byte> label = new(MaxLabelLength), retName = new(MaxDNSNameLength);
        var retOffsets = new List<Int32>();
        var value = 0;
        var state = State.LabelBegin;
        foreach (var b in bytes)
        {
            switch (state)
            {
                case State.LabelBegin:
                    if (b == PointByte)
                        throw new FormatException("不合法的 DNSName, 以 '.' 作为开头");
                    state = State.LabelIn;
                    goto case State.LabelIn;
                case State.LabelIn:
                    if (b == BackslashByte)
                        state = State.BeginEscapeNumber;
                    else if (b == PointByte)
                        goto case State.LabelEnd;
                    else
                        label.Add(b);
                    break;
                case State.LabelEnd:
                    if (label.Count > MaxLabelLength)
                        throw new FormatException("DNSName 中存在标签长度超过 63 个字节的情况");
                    retOffsets.Add(retName.Count);
                    retName.Add((Byte)label.Count);
                    retName.AddRange(label);
                    label.Clear(); // 识别完一个标签记得清空一下
                    state = State.LabelBegin;
                    break;
                case State.BeginEscapeNumber:
                    if (IsDigit(b))
                    {
                        value = b - (Byte)'0';
                        state = State.InEscapeNumber;
                    }
                    else
                    {
                        label.Add(b);
                        state = State.LabelIn;
                    }

                    break;
                case State.InEscapeNumber:
                    if (IsDigit(b))
                    {
                        value = value * 10 + b - (Byte)'0';
                        state = State.EndEscapeNumber;
                    }
                    else
                        throw new FormatException("DNSName 中存在错误的转义字符表示形式, 转义字符后面一定要有三个数字, 目前只有一个");

                    break;
                case State.EndEscapeNumber:
                    if (IsDigit(b))
                    {
                        value = value * 10 + b - (Byte)'0';
                        if (value > 255)
                            throw new FormatException("DNSName 中转义字符转义后的数字一定要小于 255");
                        label.Add((Byte)value);
                        state = State.LabelIn;
                    }

                    break;
            }
        }

        if (label.Count != 0) // 解析到最后一个 label
        {
            if (label.Count > MaxLabelLength)
                throw new FormatException("DNSName 中存在标签长度超过 63 个字节的情况");
            retOffsets.Add(retName.Count);
            retName.Add((Byte)label.Count);
            retName.AddRange(label);
        }

        retName.Add(0); // 添加尾随零

        return new Tuple<Byte[], Int32[]>(retName.ToArray(), retOffsets.ToArray());
    }
}
