// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Http;

namespace ReflectiveForms.Core.Services;

public static class CaptchaService
{
    private const string CaptchaAnswerKey = "CaptchaAnswer";
    private const string CaptchaQuestionKey = "CaptchaQuestion";

    public static (string Question, int Answer) GenerateMathCaptcha()
    {
        var random = new Random();
        var num1 = random.Next(1, 20);
        var num2 = random.Next(1, 20);
        var operation = random.Next(0, 2); // 0 = add, 1 = subtract

        string question;
        int answer;

        if (operation == 0)
        {
            question = $"{num1} + {num2}";
            answer = num1 + num2;
        }
        else
        {
            // Ensure subtraction doesn't result in negative numbers
            if (num1 < num2)
            {
                (num1, num2) = (num2, num1);
            }
            question = $"{num1} - {num2}";
            answer = num1 - num2;
        }

        return (question, answer);
    }

    public static void StoreCaptchaInSession(HttpContext context, string question, int answer)
    {
        context.Session.SetString(CaptchaQuestionKey, question);
        context.Session.SetInt32(CaptchaAnswerKey, answer);
    }

    public static bool ValidateCaptcha(HttpContext context, int userAnswer)
    {
        var correctAnswer = context.Session.GetInt32(CaptchaAnswerKey);
        var question = context.Session.GetString(CaptchaQuestionKey);

        // Log for debugging
        RfConfiguration.LogInfo("CAPTCHA validation: Question={Question}, Correct={CorrectAnswer}, User={UserAnswer}" +
            question + "-> " +  correctAnswer + " User Answered: " + userAnswer);

        // Clear the CAPTCHA from session after validation (one-time use)
        context.Session.Remove(CaptchaAnswerKey);
        context.Session.Remove(CaptchaQuestionKey);

        return correctAnswer == userAnswer;
    }

    public static string? GetCurrentCaptchaQuestion(HttpContext context)
    {
        return context.Session.GetString(CaptchaQuestionKey);
    }
}
