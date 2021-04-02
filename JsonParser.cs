/* (c) AleProjects.com, 2021
 * v.1.0
 * 
 * JSON parser
 * 
 * MIT License
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;


namespace AleProjects.Json
{
	// https://www.ietf.org/rfc/rfc4627.txt


	public class JsonParseException : Exception
	{
		public JsonParseException(int code, int line, int position, string message) : base(message)
		{
			Data.Add("Code", code);
			Data.Add("Line", line);
			Data.Add("Position", position);
		}
	}



	public class JsonDoc
	{
		protected const string ERROR_MESSAGE_UNEXPECTED_END = "Unexpected end";
		protected const string ERROR_MESSAGE_KEY_NOT_UNIQUE = "Key not unique";
		protected const string ERROR_MESSAGE_INVALID_KEY = "Invalid key";
		protected const string ERROR_MESSAGE_UNEXPECTED_TOKEN = "Unexpected token";
		protected const string ERROR_MESSAGE_UNKNOWN_ERROR = "Unknown error";

		public const int ERROR_UNEXPECTED_END = 1;
		public const int ERROR_KEY_NOT_UNIQUE = 2;
		public const int ERROR_INVALID_KEY = 3;
		public const int ERROR_UNEXPECTED_TOKEN = 4;


		protected enum NumberType
		{
			Integer = 1,
			Decimal = 2,
			Double = 3,
		}

		protected enum LastProcessedElement
		{
			TextConstant		= 0x00000001,
			Number				= 0x00000002,
			BoolConstant		= 0x00000004,
			Null				= 0x00000008,
			PrimitiveValue		= TextConstant + Number + BoolConstant + Null,

			ObjectStart			= 0x00000010,
			ObjectKey			= 0x00000020,
			KeyValueSep			= 0x00000040,
			ObjectEnd			= 0x00000080,

			ArrayStart			= 0x00000100,
			ArrayEnd			= 0x00000200,

			ListSeparator		= 0x00001000,

			Value				= PrimitiveValue + ObjectEnd + ArrayEnd
		}


		public class ParsingSettings
		{
			public bool StrictPropertyNames { get; set; }
			public bool AllowComments { get; set; }
			public bool UseNetDecimalType { get; set; }
			public bool RecognizeDateTime { get; set; }
		}



		public class JsonObject : Dictionary<string, object>
		{
			public object GetValue(string path, object defaultValue)
			{
				string[] keys = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
				object obj = this;

				for (int i = 0; i < keys.Length; i++)
					if (obj is IReadOnlyList<object> jsonArr)
					{
						if (!int.TryParse(keys[i], out int index) || index >= jsonArr.Count || index < 0)
							return defaultValue;

						obj = jsonArr[index];
					}
					else if (!(obj is JsonObject jsonObj && jsonObj.TryGetValue(keys[i], out obj)))
					{
						obj = defaultValue;
						break;
					}

				return obj;
			}

			public T GetValue<T>(string path, T defaultValue)
			{
				string[] keys = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
				object obj = this;

				for (int i = 0; i < keys.Length; i++)
					if (obj is IReadOnlyList<object> jsonArr)
					{
						if (!int.TryParse(keys[i], out int index) || index >= jsonArr.Count || index < 0)
							return defaultValue;

						obj = jsonArr[index];
					}
					else if (!(obj is JsonObject jsonObj && jsonObj.TryGetValue(keys[i], out obj)))
					{
						return defaultValue;
					}


				if (!(obj is T result))
					try
					{
						result = (T)Convert.ChangeType(obj, typeof(T));
					}
					catch
					{
						result = defaultValue;
					}

				return result;
			}

		}



		protected class ParsingContext
		{
			public List<object> JsonArray { get; set; }
			public JsonObject JsonObj { get; set; }
			public string PendingObjectKey { get; set; }
			public LastProcessedElement LastProcessed { get; set; }
			public bool IsObject { get => JsonObj != null; }
			public bool IsArray { get => JsonArray != null; }
		}


		public object Root { get; set; }


		protected static bool IsJsonWhitespace(char c)
		{
			return c == ' ' || c == '\t' || c == '\r' || c == '\n';
		}

		protected static bool IsHexDigit(char c)
		{
			return (c >= '0' && c <= '9') || ((c = char.ToUpper(c)) >= 'A' && c <= 'F');
		}

		protected static bool IsValidObjectKey(string s, bool strict)
		{
			if (string.IsNullOrWhiteSpace(s))
				return false;

			if (strict)
			{
				if (!char.IsLetter(s[0]) && s[0] != '_' && s[0] != '$')
					return false;

				for (int i = 1; i < s.Length; i++)
					if (!char.IsLetterOrDigit(s[i]) && s[0] != '_' && s[0] != '$')
						return false;
			}

			return true;
		}

		protected static object ObjectForNumber(string text, int start, int count, NumberType numberType, bool allowDecimalType)
		{
			double doubleVal;

#if NETCOREAPP2_1 || NETCOREAPP2_2 || NETCOREAPP3_0 || NETCOREAPP3_1 || NET5_0 || NETSTANDARD2_1
			
			ReadOnlySpan<char> number = text.AsSpan().Slice(start, count);

#else

			string number = text.Substring(start, count);

#endif

			switch (numberType)
			{
				case NumberType.Integer:
					if (int.TryParse(number, out int intVal))
						return intVal;

					if (long.TryParse(number, out long longVal))
						return longVal;

					if (double.TryParse(number, NumberStyles.Float, new NumberFormatInfo(), out doubleVal))
						return doubleVal;

					return null;

				case NumberType.Double:
					if (double.TryParse(number, NumberStyles.Float, new NumberFormatInfo(), out doubleVal))
						return doubleVal;

					return null;

				case NumberType.Decimal:
					NumberFormatInfo fmt = null;

					if (allowDecimalType && decimal.TryParse(number, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, fmt = new NumberFormatInfo(), out decimal decVal))
						return decVal;

					if (double.TryParse(number, NumberStyles.Float, fmt ?? new NumberFormatInfo(), out doubleVal))
						return doubleVal;

					return null;

				default:
					return null;
			}
		}

		protected static int SkipTextConstant(string text, int position, out string result)
		{
			result = null;
			StringBuilder resultBuffer = new StringBuilder(256);
			
			int Hi = text.Length - 1;
			int i = position + 1;

			while (i <= Hi)
			{
				char c = text[i];

				switch (c)
				{
					case '\r':
					case '\n':
						//end of line and not closed constant => error
						return -1;

					case '"':
						// end of text constant
						result = resultBuffer.ToString();
						return i + 1;

					case '\\':
						// escaped character

						if (++i > Hi)
							return -1;

						switch (c = text[i])
						{
							case 'r':
								i++;
								resultBuffer.Append('\r');
								break;

							case 'n':
								i++;
								resultBuffer.Append('\n');
								break;

							case 't':
								i++;
								resultBuffer.Append('\t');
								break;

							case 'b':
								i++;
								resultBuffer.Append('\b');
								break;

							case 'f':
								i++;
								resultBuffer.Append('\f');
								break;

							case '\\':
							case '"':
							case '/':
								i++;
								resultBuffer.Append(c);
								break;

							case 'u':
								if (Hi - i + 1 < 4 ||
									!IsHexDigit(text[++i]) ||
									!IsHexDigit(text[++i]) ||
									!IsHexDigit(text[++i]) ||
									!IsHexDigit(text[++i])) return -1;

								i++;

#if NETCOREAPP2_1 || NETCOREAPP2_2 || NETCOREAPP3_0 || NETCOREAPP3_1 || NET5_0 || NETSTANDARD2_1
								resultBuffer.Append((char)ushort.Parse(text.AsSpan().Slice(i - 4, 4), NumberStyles.HexNumber));
#else
								resultBuffer.Append((char)ushort.Parse(text.Substring(i - 4, 4), NumberStyles.HexNumber));
#endif

								break;

							default:
								return -1;
						}

						break;

					default:
						i++;
						resultBuffer.Append(c);
						break;
				}
			}

			return -1;
		}

		protected static int SkipNumber(string text, int position, out NumberType numberType)
		{
			numberType = NumberType.Integer;

			int Hi = text.Length - 1;
			int i = position;

			if (i > Hi || i < 0)
				return -1;

			/* 
				number = [ minus ] int [ frac ] [ exp ]

				decimal-point = %x2E       ; .
				digit1-9 = %x31-39         ; 1-9
				e = %x65 / %x45            ; e E
				exp = e [ minus / plus ] 1*DIGIT
				frac = decimal-point 1*DIGIT
				int = zero / ( digit1-9 *DIGIT )
				minus = %x2D               ; -
				plus = %x2B                ; +
				zero = %x30                ; 0
			*/


			// minus part

			if (text[i] == '-')
			{
				if (++i > Hi)
					return -1;
			}

			char c;


			// int part

			if (text[i] != '0')
			{
				int j = i;

				while (i <= Hi && (c = text[i]) >= '0' && c <= '9') i++;

				if (i == j)
					return -1;
			}
			else i++;

			if (i > Hi)
				return i;

			c = text[i];


			// frac part

			if (c == '.')
			{
				int j = ++i;

				while (i <= Hi && (c = text[i]) >= '0' && c <= '9') i++;

				if (i == j)
					return -1;

				numberType = NumberType.Decimal;

				if (i > Hi)
					return i;

				c = text[i];
			}


			// exp part

			if (char.ToUpper(c) == 'E')
			{
				int k = i;

				if (++i > Hi)
					return k;

				c = text[i];

				bool hasSign = c == '+' || c == '-';

				if (hasSign && ++i > Hi)
					return -1;

				int j = i;

				while (i <= Hi && (c = text[i]) >= '0' && c <= '9') i++;

				if (i == j)
					if (hasSign)
						return -1;
					else
					{
						numberType = NumberType.Decimal;
						return k;
					}
				else numberType = NumberType.Double;
			}

			return i;
		}

		protected static bool TryGetDateTime(string text, out DateTime dt)
		{
			if (text.Length == "0000-00-00T00:00:00.000Z".Length &&
				char.IsNumber(text[0]) && text[4] == '-' && text[10] == 'T' && text[23] == 'Z' &&
				DateTime.TryParse(text, out dt))
				return true;

			if (text.Length > 8 &&
				text[0] == '/' &&
				text.StartsWith("/Date(") && 
				text.EndsWith(")/"))
			{

#if NETCOREAPP2_1 || NETCOREAPP2_2 || NETCOREAPP3_0 || NETCOREAPP3_1 || NET5_0 || NETSTANDARD2_1

				if (long.TryParse(text.AsSpan().Slice("/Date(".Length, text.Length - "/Date()/".Length), out long msec))
				{
					double ms = msec;

					if (msec >= 0)
						if (DateTime.MaxValue.Subtract(DateTime.UnixEpoch).TotalMilliseconds < ms)
							dt = DateTime.UnixEpoch.AddMilliseconds(ms);
						else
							dt = DateTime.MaxValue;
					else if (DateTime.UnixEpoch.Subtract(DateTime.MinValue).TotalMilliseconds < -ms)
						dt = DateTime.UnixEpoch.AddMilliseconds(ms);
					else
						dt = DateTime.MinValue;

					return true;
				}

#else

				if (long.TryParse(text.Substring("/Date(".Length, text.Length - "/Date()/".Length), out long msec))
				{
					double ms = msec;
					DateTime unixEpoch = new DateTime(1970, 1, 1);

					if (msec >= 0)
						if (DateTime.MaxValue.Subtract(unixEpoch).TotalMilliseconds < ms)
							dt = unixEpoch.AddMilliseconds(ms);
						else
							dt = DateTime.MaxValue;
					else if (unixEpoch.Subtract(DateTime.MinValue).TotalMilliseconds < -ms)
						dt = unixEpoch.AddMilliseconds(ms);
					else
						dt = DateTime.MinValue;

					return true;
				}

#endif
			}

			dt = DateTime.MinValue;
			return false;
		}

		protected static JsonParseException CreateException(int code, string text, int index)
		{
			if (index >= text.Length) 
				index = text.Length - 1;

			int line = 0;
			int position = 0;

			while (index >= 0)
			{
				if (text[index] == '\n') 
					line++;

				if (line == 0)
					position++;

				index--;
			}

			string message;

			switch (code)
			{
				case ERROR_UNEXPECTED_END:
					message = ERROR_MESSAGE_UNEXPECTED_END;
					break;

				case ERROR_KEY_NOT_UNIQUE:
					message = ERROR_MESSAGE_KEY_NOT_UNIQUE;
					break;

				case ERROR_INVALID_KEY:
					message = ERROR_MESSAGE_INVALID_KEY;
					break;

				case ERROR_UNEXPECTED_TOKEN:
					message = ERROR_MESSAGE_UNEXPECTED_TOKEN;
					break;

				default:
					message = ERROR_MESSAGE_UNKNOWN_ERROR;
					break;
			}

			return new JsonParseException(code, line, position, message);
		}


		protected JsonDoc() { }

		public static JsonDoc Parse(string text, ParsingSettings settings = null)
		{
			if (text == null)
				throw new ArgumentNullException(nameof(text));

			if (string.IsNullOrWhiteSpace(text))
				throw new ArgumentException(ERROR_MESSAGE_UNEXPECTED_END, nameof(text));

			bool strictPropertyNames = true;
			bool allowComments = false;
			bool useDecimalType = false;
			bool recognizeDateTime = false;

			if (settings != null)
			{
				strictPropertyNames = settings.StrictPropertyNames;
				allowComments = settings.AllowComments;
				useDecimalType = settings.UseNetDecimalType;
				recognizeDateTime = settings.RecognizeDateTime;
			}


			Stack<ParsingContext> parsingContext = new Stack<ParsingContext>();
			object root = null;
			int Hi = text.Length - 1;
			int i = 0;
			char c;


			while (i <= Hi)
			{
				c = text[i];

				if (IsJsonWhitespace(c))
				{
					while (++i <= Hi && IsJsonWhitespace(text[i])) ;
				}
				else if (c == '"')
				{
					int j = SkipTextConstant(text, i, out string val);

					if (j < 0)
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					if (root == null)
					{
						if (recognizeDateTime && TryGetDateTime(val, out DateTime dt)) root = dt;
						else root = val;
					}
					else if (!parsingContext.TryPeek(out ParsingContext ctx))
					{
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
					}
					else if (ctx.IsArray)
					{
						if (ctx.LastProcessed == LastProcessedElement.ArrayStart || 
							ctx.LastProcessed == LastProcessedElement.ListSeparator)
						{
							if (recognizeDateTime && TryGetDateTime(val, out DateTime dt)) ctx.JsonArray.Add(dt);
							else ctx.JsonArray.Add(val);

							ctx.LastProcessed = LastProcessedElement.TextConstant;
						}
						else
						{
							throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
						}
					}
					else
					{
						if (ctx.LastProcessed == LastProcessedElement.ObjectKey || 
							(ctx.LastProcessed & LastProcessedElement.Value) != 0)
							throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);


						if (ctx.LastProcessed == LastProcessedElement.ObjectStart || 
							ctx.LastProcessed == LastProcessedElement.ListSeparator)
						{
							if (!IsValidObjectKey(val, strictPropertyNames))
								throw CreateException(ERROR_INVALID_KEY, text, i);

							if (ctx.JsonObj.ContainsKey(val))
								throw CreateException(ERROR_KEY_NOT_UNIQUE, text, i);

							ctx.PendingObjectKey = val;
							ctx.LastProcessed = LastProcessedElement.ObjectKey;
						}
						else
						{
							if (recognizeDateTime && TryGetDateTime(val, out DateTime dt)) ctx.JsonObj[ctx.PendingObjectKey] = dt;
							else ctx.JsonObj[ctx.PendingObjectKey] = val;

							ctx.LastProcessed = LastProcessedElement.TextConstant;
						}
					}

					i = j;
				}
				else if (c == ',')
				{
					if (!parsingContext.TryPeek(out ParsingContext ctx) || 
						(ctx.LastProcessed & LastProcessedElement.Value) == 0)
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					ctx.LastProcessed = LastProcessedElement.ListSeparator;
					i++;
				}
				else if (c == ':')
				{
					if (!parsingContext.TryPeek(out ParsingContext ctx) || 
						ctx.LastProcessed != LastProcessedElement.ObjectKey)
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					ctx.LastProcessed = LastProcessedElement.KeyValueSep;
					i++;
				}
				else if (c == '{')
				{
					if (parsingContext.TryPeek(out ParsingContext ctx) &&
						ctx.LastProcessed != LastProcessedElement.KeyValueSep &&
						ctx.LastProcessed != LastProcessedElement.ArrayStart &&
						(ctx.LastProcessed != LastProcessedElement.ListSeparator || ctx.IsObject))
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					parsingContext.Push(ctx = new ParsingContext() { JsonObj = new JsonObject(), LastProcessed = LastProcessedElement.ObjectStart });

					if (root == null)
						root = ctx.JsonObj;

					i++;
				}
				else if (c == '}')
				{
					if (!parsingContext.TryPeek(out ParsingContext ctx) ||
						!ctx.IsObject ||
						(ctx.LastProcessed != LastProcessedElement.ObjectStart && (ctx.LastProcessed & LastProcessedElement.Value) == 0))
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					JsonObject obj = ctx.JsonObj;

					parsingContext.Pop();

					if (parsingContext.TryPeek(out ctx))
					{
						if (ctx.IsArray)
							ctx.JsonArray.Add(obj);
						else
							ctx.JsonObj[ctx.PendingObjectKey] = obj;

						ctx.LastProcessed = LastProcessedElement.ObjectEnd;
					}

					i++;
				}
				else if (c == '[')
				{
					if (parsingContext.TryPeek(out ParsingContext ctx) &&
						ctx.LastProcessed != LastProcessedElement.KeyValueSep &&
						ctx.LastProcessed != LastProcessedElement.ListSeparator)
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					parsingContext.Push(ctx = new ParsingContext() { JsonArray = new List<object>(), LastProcessed = LastProcessedElement.ArrayStart });

					if (root == null)
						root = ctx.JsonArray;

					i++;
				}
				else if (c == ']')
				{
					if (!parsingContext.TryPeek(out ParsingContext ctx) ||
						!ctx.IsArray ||
						(ctx.LastProcessed != LastProcessedElement.ArrayStart && (ctx.LastProcessed & LastProcessedElement.Value) == 0))
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					IReadOnlyList<object> array = ctx.JsonArray;

					parsingContext.Pop();

					if (parsingContext.TryPeek(out ctx))
					{
						if (ctx.IsArray)
							ctx.JsonArray.Add(array);
						else
							ctx.JsonObj[ctx.PendingObjectKey] = array;

						ctx.LastProcessed = LastProcessedElement.ArrayEnd;
					}

					i++;
				}
				else if (c == '-' || (c >= '0' && c <= '9'))
				{
					int j = SkipNumber(text, i, out NumberType numberType);

					if (j < 0)
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					if (root == null)
					{
						root = ObjectForNumber(text, i, j - i, numberType, useDecimalType);
					}
					else if (!parsingContext.TryPeek(out ParsingContext ctx))
					{
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
					}
					else if (ctx.IsArray)
					{
						if (ctx.LastProcessed == LastProcessedElement.ArrayStart ||
							ctx.LastProcessed == LastProcessedElement.ListSeparator)
						{
							ctx.JsonArray.Add(ObjectForNumber(text, i, j - i, numberType, useDecimalType));
							ctx.LastProcessed = LastProcessedElement.Number;
						}
						else
						{
							throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
						}
					}
					else
					{
						if (ctx.LastProcessed != LastProcessedElement.KeyValueSep)
							throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

						ctx.JsonObj[ctx.PendingObjectKey] = ObjectForNumber(text, i, j - i, numberType, useDecimalType);
						ctx.LastProcessed = LastProcessedElement.Number;
					}

					i = j;
				}
				else if (c == 'n')
				{
					if (string.Compare(text, i, "null", 0, 4, StringComparison.InvariantCulture) != 0)
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					if (root == null)
					{
						root = null;
					}
					else if (!parsingContext.TryPeek(out ParsingContext ctx))
					{
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
					}
					else if (ctx.IsArray)
					{
						if (ctx.LastProcessed == LastProcessedElement.ArrayStart ||
							ctx.LastProcessed == LastProcessedElement.ListSeparator)
						{
							ctx.JsonArray.Add(null);
							ctx.LastProcessed = LastProcessedElement.Null;
						}
						else
						{
							throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
						}
					}
					else
					{
						if (ctx.LastProcessed != LastProcessedElement.KeyValueSep)
							throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

						ctx.JsonObj[ctx.PendingObjectKey] = null;
						ctx.LastProcessed = LastProcessedElement.Null;
					}

					i += 4;
				}
				else if (c == 'f' || c == 't')
				{
					bool val;

					if (string.Compare(text, i, "false", 0, 5, StringComparison.InvariantCulture) == 0)
					{
						val = false;
						i += 5;
					}
					else if (string.Compare(text, i, "true", 0, 4, StringComparison.InvariantCulture) == 0)
					{
						val = true;
						i += 4;
					}
					else
					{
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
					}

					if (root == null)
					{
						root = val;
					}
					else if (!parsingContext.TryPeek(out ParsingContext ctx))
					{
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
					}
					else if (ctx.IsArray)
					{
						if (ctx.LastProcessed == LastProcessedElement.ArrayStart ||
							ctx.LastProcessed == LastProcessedElement.ListSeparator)
						{
							ctx.JsonArray.Add(val);
							ctx.LastProcessed = LastProcessedElement.BoolConstant;
						}
						else
						{
							throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
						}
					}
					else
					{
						if (ctx.LastProcessed != LastProcessedElement.KeyValueSep)
							throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

						ctx.JsonObj[ctx.PendingObjectKey] = val;
						ctx.LastProcessed = LastProcessedElement.BoolConstant;
					}
				}
				else if (c == '/')
				{
					if (!allowComments || i == Hi || ((c = text[++i]) != '/' && c != '*'))
						throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);

					if (c == '/')
					{
						do
						{
							i++;
						} while (i <= Hi && text[i] != '\n');
					}
					else
					{
						do
						{
							i++;
						} while (i < Hi && text[i] != '*' && text[i + 1] != '/');

						if (i >= Hi)
							throw CreateException(ERROR_UNEXPECTED_END, text, i);
					}
				}
				else
				{
					throw CreateException(ERROR_UNEXPECTED_TOKEN, text, i);
				}
			}


			if (parsingContext.Count != 0)
				throw CreateException(ERROR_UNEXPECTED_END, text, i);

			return new JsonDoc() { Root = root };
		}
	}

}
