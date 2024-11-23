using System.Text;

namespace CSDNS;

public class DNSName
{
    private enum State
    {
        LabelBegin, // 域名中一个标签开始
        LabelIn, // 在一个标签处理过程中
        LabelEnd, //域名中一个标签结束

        // 解析转义字符后面的数字, 一定解析后面的三个字符, 如果不到三个字符则抛出异常, 到了三个字符就进入到上面的三个状
        BeginEscapeNumber,
        InEscapeNumber, // 进入对转移字符转义的状态
        EndEscapeNumber, // 对数字进行转义
    }

    /// <summary>
    ///  定义一些程序用到的常量:
    ///   * `MaxDNSNameLength` 表示一整个 DNSName 的长度不应该超过 255 个字节;
    ///   * `MaxLabelLength` 表示, DNSName 中的一个标签不应该超过 63 个字节
    /// </summary>
    public const UInt16 MaxDNSNameLength = 255, MaxLabelLength = 63;

    private const Byte
        PointByte = (Byte)'.',
        EndByte = (Byte)'\0',
        // 这个 Backslash 是字符名称, 但是不少项目使用的状态名称含有 `Escape`, 其实指的就是 `Escape Characters` 转义字符
        BackslashByte = (Byte)'\\';

    private Byte[] name;

    public DNSName(Byte[] bytes)
    {
        if (bytes.Length == 0 || bytes is [PointByte])
            name = [EndByte];
        else
            name = ResolveDNSName(bytes);
    }

    private Boolean IsDigit(Byte b) => b is >= (Byte)'0' and <= (Byte)'9';

    private Boolean IsRoot() => name[0] == EndByte;

    private Boolean Empty() => name.Length == 0;

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
        var i = 0;
        while (i < name.Length && name[i] != EndByte)
        {
            var labelLength = name[i];
            ToLabel(stringBuilder, i + 1, labelLength);
            stringBuilder.Append(separator);
            i += labelLength + 1;
        }

        if (!trailing)
            stringBuilder.Length -= separator.Length;

        return stringBuilder.ToString();
    }

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
    /// <example>www.bupt.edu.cn -> 3www4bupt3edu2cn0</example>
    /// <exception cref="FormatException"></exception>
    private Byte[] ResolveDNSName(Byte[] bytes)
    {
        List<Byte> label = new(MaxLabelLength), ret = new(MaxDNSNameLength);
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
                    {
                        state = State.BeginEscapeNumber;
                        goto case State.BeginEscapeNumber;
                    }
                    else if (b == PointByte)
                    {
                        state = State.LabelEnd; // 实际上这里的赋值是毫无意义的
                        goto case State.LabelEnd;
                    }
                    else
                        label.Add(b);

                    break;
                case State.LabelEnd:
                    if (label.Count > MaxLabelLength)
                        throw new FormatException("DNSName 中存在标签长度超过 63 个字节的情况");
                    ret.Add((Byte)label.Count);
                    ret.AddRange(label);
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
            ret.Add((Byte)label.Count);
            ret.AddRange(label);
        }

        ret.Add(0); // 添加尾随零

        return ret.ToArray();
    }
}