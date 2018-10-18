这是一个.Net API 注入例程

没有使用修改IL的方法来实现，

而是通过生成动态方法(DynamicMethod)的方式。

具体的说就是：
1、解析目标API的MethodInfo，生成签名一致（包括参数名称）的动态方法
2、解析目标API的方法体指令，通过Transpiler替换API方法体指令生成动态方法的方法体
3、获取目标API的运行时地址，将起始字节修改为jmp指令到生成的动态函数

在此实现中允许添加Prolog方法（prefix）和Epilog方法（postfix）

Prolog返回void或bool, 
Prolog返回true时继续调用API方法体指令，返回false时跳转到Epilog


对于实例方法，Prolog方法（prefix）和Epilog方法（postfix）的签名可以是：
1、static void/bool Prolog(ThisType @this) 或 void/bool Prolog();
2、static ReturnType Epilog(ThisType @this) 或 ReturnType Epilog();
但通常来说实例方法不常使用，因为注入自身方法并不是常见的行为

对于P/Invoke方法
1、可以使用WINAPI的Detour方法，直接注入WINAPI
2、不使用Detour方法时，可以直接写入跳转指令

