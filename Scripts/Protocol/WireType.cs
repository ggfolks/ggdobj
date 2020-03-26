namespace GGFolks.Protocol {

/// <summary>
/// Defines the four basic wire types: varint (used for booleans, chars, enums, and all integer
/// types), four byte (used for single-precision floats), eight byte (double precision floats),
/// and byte length encoded (everything else). These types provide just enough information to skip
/// unrecognized fields.
/// </summary>
public enum WireType { VarInt, FourByte, EightByte, ByteLength }

}
