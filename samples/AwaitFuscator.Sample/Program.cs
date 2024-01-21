internal static class Program
{
    public static void Main()
    {
        Console.WriteLine("Hello, world!");

        while (true)
        {
            Console.Write("What is your name [Leave empty to exit]?: ");
            string? name = Console.ReadLine();
            if (string.IsNullOrEmpty(name))
                break;

            Console.WriteLine($"Hi {name}. That's a cool name!");

            int age = AskInteger("How old are you?: ");
            if (age < 21)
                Console.WriteLine("You are too young to drink!");
            else
                Console.WriteLine("You are allowed to drink!");
        }

        Console.WriteLine("See you around!");
    }

    public static int AskInteger(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            string? input = Console.ReadLine();
            if (string.IsNullOrEmpty(input))
                continue;

            if (!int.TryParse(input, out int age))
                continue;

            return age;
        }
    }
}