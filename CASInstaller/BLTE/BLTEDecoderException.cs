namespace CASInstaller.BLTE;

[Serializable]
public class BLTEDecoderException(int error, string message) : Exception(message)
{
    public int ErrorCode { get; } = error;
}